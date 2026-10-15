using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace OpenClawDebugger;

public sealed record CustomArtifactMetadata(string Name, string Description, string Version);

public static class CustomArtifactArchiveReader
{
    internal const long MaximumMetadataBytes = 1024 * 1024;
    internal const int MaximumDescriptionLength = 2000;
    private static readonly string[] PluginManifestNames =
    [
        "openclaw.plugin.json", "plugin.json", ".claude-plugin/plugin.json",
        ".cursor-plugin/plugin.json", ".codex-plugin/plugin.json"
    ];

    public static CustomArtifactMetadata Read(string path, string type, string fileName)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return type switch
        {
            "plugin" => ReadPlugin(archive, fileName),
            "skill" => ReadSkill(archive, fileName),
            _ => throw new InvalidDataException("自定义项目类型无效。")
        };
    }

    private static CustomArtifactMetadata ReadPlugin(ZipArchive archive, string fileName)
    {
        var manifests = archive.Entries.Where(entry => PluginManifestNames.Any(name => HasArchivePath(entry, name))).ToArray();
        if (manifests.Length != 1) throw new InvalidDataException("插件 ZIP 包必须且只能包含一个 OpenClaw 或兼容插件清单。");
        using var document = JsonDocument.Parse(ReadEntryText(manifests[0]));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("插件清单必须是 JSON 对象。");
        var name = StringProperty(root, "name") ?? StringProperty(root, "id") ?? Path.GetFileNameWithoutExtension(fileName);
        var description = StringProperty(root, "description") ?? StringProperty(root, "summary") ?? "";
        var version = StringProperty(root, "version") ?? "";
        return ValidateMetadata(name, description, version);
    }

    private static CustomArtifactMetadata ReadSkill(ZipArchive archive, string fileName)
    {
        var skillFiles = archive.Entries.Where(entry =>
            Path.GetFileName(entry.FullName).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (skillFiles.Length != 1) throw new InvalidDataException("技能 ZIP 包必须且只能包含一个 SKILL.md。");
        var text = ReadEntryText(skillFiles[0]);
        var folder = Path.GetFileName(Path.GetDirectoryName(skillFiles[0].FullName) ?? "");
        var name = ReadFrontmatterValue(text, "name") ??
            (string.IsNullOrWhiteSpace(folder) ? Path.GetFileNameWithoutExtension(fileName) : folder);
        var description = ReadFrontmatterValue(text, "description") ?? "";
        var version = ReadFrontmatterValue(text, "version") ?? "";
        return ValidateMetadata(name, description, version);
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        if (entry.Length > MaximumMetadataBytes) throw new InvalidDataException("ZIP 包中的元数据文件超过 1 MiB。");
        using var source = entry.Open();
        using var destination = new MemoryStream();
        source.CopyTo(destination);
        if (destination.Length > MaximumMetadataBytes) throw new InvalidDataException("ZIP 包中的元数据文件超过 1 MiB。");
        return new UTF8Encoding(false, true).GetString(destination.ToArray());
    }

    private static string? ReadFrontmatterValue(string text, string key)
    {
        var lines = text.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---") return null;
        foreach (var line in lines.Skip(1).TakeWhile(value => value.Trim() != "---"))
        {
            var separator = line.IndexOf(':');
            if (separator < 1 || !line[..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            return line[(separator + 1)..].Trim().Trim('"', '\'');
        }
        return null;
    }

    private static bool HasArchivePath(ZipArchiveEntry entry, string expected) =>
        entry.FullName.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
        entry.FullName.EndsWith("/" + expected, StringComparison.OrdinalIgnoreCase);

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static CustomArtifactMetadata ValidateMetadata(string name, string description, string version)
    {
        name = name.Trim();
        description = description.Trim();
        version = version.Trim();
        if (name.Length is < 1 or > 180) throw new InvalidDataException("自定义项目名称需为 1 到 180 个字符。");
        if (description.Length > MaximumDescriptionLength) throw new InvalidDataException("项目介绍超过 2000 个字符。");
        if (version.Length > 120) throw new InvalidDataException("项目版本超过 120 个字符。");
        return new CustomArtifactMetadata(name, description, version);
    }
}
