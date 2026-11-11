using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenClawDebugger;

public sealed record SnapshotInfo(string Id, string Root, string RelativePath, string Sha256, DateTimeOffset CreatedUtc, string EncryptedFile);

/// <summary>
/// 回滚快照的本地存储。加密算法由 <see cref="ISecretProtector"/> 提供，
/// 文件后缀保持 .dpapi 不变，避免 Windows 上已有的历史快照失效。
/// </summary>
public sealed class LocalSnapshotStore(string privateDirectory, ISecretProtector secretProtector)
{
    private readonly string _root = Path.Combine(privateDirectory, "Rollback");

    public string RootDirectory => _root;

    public async Task<SnapshotInfo> SaveAsync(string root, string relativePath, byte[] content, string sha256)
    {
        Directory.CreateDirectory(_root);
        var id = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}_{Guid.NewGuid():N}";
        var encryptedName = id + ".dpapi";
        var encryptedPath = Path.Combine(_root, encryptedName);
        var metadataPath = Path.Combine(_root, id + ".json");
        var protectedBytes = secretProtector.Protect(content);
        var temporary = encryptedPath + ".tmp";
        await File.WriteAllBytesAsync(temporary, protectedBytes);
        File.Move(temporary, encryptedPath, overwrite: false);
        var info = new SnapshotInfo(id, root, relativePath, sha256, DateTimeOffset.UtcNow, encryptedName);
        var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, json, new UTF8Encoding(false));
        return info;
    }

    public async Task<byte[]> ReadAsync(SnapshotInfo info)
    {
        var encryptedPath = Path.Combine(_root, Path.GetFileName(info.EncryptedFile));
        return secretProtector.Unprotect(await File.ReadAllBytesAsync(encryptedPath));
    }
}
