using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenClawDebugger;

public sealed record CustomArtifact(
    string Id,
    string Type,
    string Name,
    string Description,
    string Version,
    string FileName,
    long Size,
    string Sha256,
    DateTimeOffset UploadedAtUtc);

public sealed class CustomArtifactService
{
    private const long MaximumArchiveBytes = 100L * 1024 * 1024;
    private const int CopyBufferSizeBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private static readonly string[] PluginManifestNames =
    [
        "openclaw.plugin.json", "plugin.json", ".claude-plugin/plugin.json",
        ".cursor-plugin/plugin.json", ".codex-plugin/plugin.json"
    ];

    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CustomArtifactService(string privateDirectory)
    {
        _root = Path.Combine(Path.GetFullPath(privateDirectory), "CustomArtifacts");
    }

    public async Task<object> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var artifacts = await ReadAllAsync(cancellationToken);
            return new
            {
                plugins = artifacts.Where(item => item.Type == "plugin").ToArray(),
                skills = artifacts.Where(item => item.Type == "skill").ToArray()
            };
        }
        finally { _gate.Release(); }
    }

    public async Task<object> UploadAsync(JsonElement payload, string sourcePath, CancellationToken cancellationToken)
    {
        var type = RequiredString(payload, "type");
        var fileName = RequiredString(payload, "fileName");
        var size = RequiredInt64(payload, "size");
        ValidateUpload(type, fileName, size, sourcePath);
        await _gate.WaitAsync(cancellationToken);
        try { return await SaveUploadAsync(type, fileName, size, sourcePath, cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task<object> DeleteAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var id = RequiredString(payload, "id");
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("自定义项目 ID 无效。");
        await _gate.WaitAsync(cancellationToken);
        try { return await DeleteCoreAsync(id, cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task<CustomArtifact> SaveUploadAsync(
        string type, string fileName, long size, string sourcePath, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(_root, type);
        Directory.CreateDirectory(directory);
        var stagingPath = Path.Combine(directory, ".upload-" + id + ".part");
        var storedPath = Path.Combine(directory, id + ".zip");
        try
        {
            var sha256 = await CopyAndHashAsync(sourcePath, stagingPath, size, cancellationToken);
            var metadata = CustomArtifactArchiveReader.Read(stagingPath, type, fileName);
            var artifact = new CustomArtifact(
                id, type, metadata.Name, metadata.Description, metadata.Version,
                fileName, size, sha256, DateTimeOffset.UtcNow);
            File.Move(stagingPath, storedPath);
            var artifacts = await ReadAllAsync(cancellationToken);
            await WriteAllAsync([artifact, .. artifacts], cancellationToken);
            return artifact;
        }
        catch
        {
            DeleteIfPresent(stagingPath);
            DeleteIfPresent(storedPath);
            throw;
        }
    }

    private async Task<string> CopyAndHashAsync(
        string sourcePath, string stagingPath, long expectedSize, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            CopyBufferSizeBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            CopyBufferSizeBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[CopyBufferSizeBytes];
        long copied = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            copied += read;
            if (copied > expectedSize) throw new IOException("上传文件在读取期间发生变化。");
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (copied != expectedSize) throw new IOException("上传文件长度与所选文件不一致。");
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private async Task<object> DeleteCoreAsync(string id, CancellationToken cancellationToken)
    {
        var artifacts = await ReadAllAsync(cancellationToken);
        var artifact = artifacts.SingleOrDefault(item => item.Id == id)
            ?? throw new FileNotFoundException("自定义列表中找不到该项目。");
        var source = ArtifactPath(artifact);
        var pendingDelete = source + ".deleting";
        File.Move(source, pendingDelete);
        try
        {
            await WriteAllAsync(artifacts.Where(item => item.Id != id).ToArray(), cancellationToken);
        }
        catch
        {
            if (File.Exists(pendingDelete) && !File.Exists(source)) File.Move(pendingDelete, source);
            throw;
        }
        File.Delete(pendingDelete);
        return new { deleted = true, id };
    }

    private async Task<List<CustomArtifact>> ReadAllAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_root, "index.json");
        if (!File.Exists(path)) return [];
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        var artifacts = JsonSerializer.Deserialize<List<CustomArtifact>>(text, JsonOptions)
            ?? throw new InvalidDataException("自定义列表数据为空或格式无效。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in artifacts)
        {
            if (artifact is null) throw new InvalidDataException("自定义列表包含空记录。");
            ValidateRecord(artifact);
            if (!ids.Add(artifact.Id)) throw new InvalidDataException("自定义列表存在重复项目 ID：" + artifact.Id);
            if (!File.Exists(ArtifactPath(artifact)))
                throw new FileNotFoundException("自定义项目文件缺失：" + artifact.FileName);
        }
        return artifacts;
    }

    private async Task WriteAllAsync(IReadOnlyList<CustomArtifact> artifacts, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "index.json");
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var json = JsonSerializer.Serialize(artifacts, JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryPath, path, true);
        }
        finally { DeleteIfPresent(temporaryPath); }
    }

    private string ArtifactPath(CustomArtifact artifact)
    {
        ValidateRecord(artifact);
        return Path.Combine(_root, artifact.Type, artifact.Id + ".zip");
    }

    private static void ValidateRecord(CustomArtifact artifact)
    {
        if (artifact.Id is null || artifact.Type is null || artifact.Name is null || artifact.Description is null ||
            artifact.Version is null || artifact.FileName is null || artifact.Sha256 is null ||
            !Guid.TryParseExact(artifact.Id, "N", out _) || artifact.Type is not ("plugin" or "skill") ||
            artifact.Name.Length is < 1 or > 180 || artifact.Description.Length > CustomArtifactArchiveReader.MaximumDescriptionLength ||
            artifact.Version.Length > 120 || artifact.FileName.Length is < 1 or > 180 ||
            artifact.Size is < 1 or > MaximumArchiveBytes || artifact.Sha256.Length != 64 ||
            artifact.Sha256.Any(value => !Uri.IsHexDigit(value)))
            throw new InvalidDataException("自定义项目索引包含无效记录。");
    }

    private static void ValidateUpload(string type, string fileName, long size, string sourcePath)
    {
        if (type is not ("plugin" or "skill")) throw new InvalidDataException("请选择插件或技能类型。");
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 180 || Path.GetFileName(fileName) != fileName ||
            fileName.Any(char.IsControl) || Path.GetExtension(fileName).Equals(".zip", StringComparison.OrdinalIgnoreCase) is false)
            throw new InvalidDataException("请选择 ZIP 格式的插件包或技能包。");
        if (size is < 1 or > MaximumArchiveBytes) throw new InvalidDataException("ZIP 包大小需在 1 B 到 100 MiB 之间。");
        var source = new FileInfo(sourcePath);
        if (!source.Exists || source.Length != size) throw new IOException("无法读取所选 ZIP 包，或文件在上传前发生变化。");
    }

    private static string RequiredString(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("自定义上传参数无效：" + name);
        return value.GetString() ?? "";
    }

    private static long RequiredInt64(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
            throw new InvalidDataException("自定义上传参数无效：" + name);
        return number;
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
