using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenClawDebugger;

/// <summary>Validates community catalog requests and delegates ClawHub reads and security report checks.</summary>
public sealed class PluginCommunityService(IClawHubPluginClient catalog, IPluginSecurityScanner securityScanner)
{
    private static readonly HashSet<string> SortModes = new(StringComparer.Ordinal)
    {
        "recommended", "downloads", "updated"
    };

    public Task<JsonNode> BrowseAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var sort = OptionalString(payload, "sort") ?? "recommended";
        if (!SortModes.Contains(sort)) throw new InvalidDataException("插件社区排序方式无效。");
        var cursor = OptionalString(payload, "cursor");
        return catalog.BrowseAsync(sort, cursor, OptionalBoolean(payload, "forceRefresh"), cancellationToken);
    }

    public Task<JsonNode> SearchAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var query = RequiredString(payload, "query").Trim();
        if (query.Length == 0) throw new InvalidDataException("请输入插件搜索词。");
        return catalog.SearchAsync(query, OptionalBoolean(payload, "forceRefresh"), cancellationToken);
    }

    public Task<JsonNode> CheckSecurityAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var packageName = ValidatePackageName(RequiredString(payload, "packageName"));
        var version = ValidateVersion(RequiredString(payload, "version"));
        return securityScanner.CheckAsync(packageName, version, OptionalBoolean(payload, "forceRefresh"), cancellationToken);
    }

    private static string RequiredString(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("插件社区参数无效：" + name);
        return value.GetString() ?? "";
    }

    private static string? OptionalString(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException("插件社区参数无效：" + name);
        return value.GetString();
    }

    private static bool OptionalBoolean(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("插件社区参数无效：" + name);
        return value.GetBoolean();
    }

    private static string ValidatePackageName(string packageName)
    {
        if (packageName.Length == 0 || packageName.StartsWith("-", StringComparison.Ordinal) ||
            packageName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('@' or '.' or '_' or '/' or '-')) ||
            packageName.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("ClawHub 插件包名格式无效。");
        return packageName;
    }

    private static string ValidateVersion(string version)
    {
        if (version.Length == 0 || version.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '+' or '_')))
            throw new InvalidDataException("插件版本格式无效。");
        return version;
    }
}
