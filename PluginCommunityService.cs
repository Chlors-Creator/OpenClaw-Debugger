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
        return catalog.BrowseAsync(new ClawHubBrowseRequest(sort, cursor, OptionalBoolean(payload, "forceRefresh")), cancellationToken);
    }

    public Task<JsonNode> SearchAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var query = RequiredString(payload, "query").Trim();
        if (query.Length == 0) throw new InvalidDataException("请输入插件搜索词。");
        return catalog.SearchAsync(new ClawHubSearchRequest(query, OptionalBoolean(payload, "forceRefresh")), cancellationToken);
    }

    public Task<JsonNode> CheckSecurityAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var packageName = ValidatePackageName(RequiredString(payload, "packageName"));
        var version = ValidateVersion(RequiredString(payload, "version"));
        var request = new PluginSecurityCheck(packageName, version, OptionalBoolean(payload, "forceRefresh"));
        return securityScanner.CheckAsync(request, cancellationToken);
    }

    public async Task<JsonNode> LookupInstalledPluginCategoriesAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        if (!payload.TryGetProperty("plugins", out var plugins) || plugins.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("插件分类查询参数无效：plugins");
        var entries = plugins.EnumerateArray().Select(plugin => plugin.Clone()).ToArray();
        if (entries.Any(plugin => plugin.ValueKind != JsonValueKind.Object))
            throw new InvalidDataException("插件分类查询项必须是对象。");
        var results = await Task.WhenAll(entries.Select(plugin => LookupPluginCategoryAsync(plugin, cancellationToken)));
        return new JsonObject { ["plugins"] = new JsonArray(results) };
    }

    private async Task<JsonNode> LookupPluginCategoryAsync(JsonElement plugin, CancellationToken cancellationToken)
    {
        var pluginId = ReadPluginText(plugin, "id", "pluginId");
        var package = ReadPluginText(plugin, "packageName", "installRef", "package.name", "source.packageName", "source.spec");
        var source = ReadPluginText(plugin, "source");
        var packageReference = NormalizePackageReference(package) ?? NormalizePackageReference(source);
        var identifiers = new[] { packageReference, pluginId, ReadPluginText(plugin, "name", "displayName", "title") }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (identifiers.Length == 0) return CreateLookupResult(pluginId, "error", null, "插件缺少可用于 ClawHub 精确匹配的 ID 或包名。");
        foreach (var query in identifiers)
        {
            try
            {
                var response = await catalog.SearchAsync(new ClawHubSearchRequest(query, false), cancellationToken);
                var match = FindExactMatch(response, identifiers);
                if (match is not null) return CreateLookupResult(pluginId, "matched", ReadCategoryData(match), null);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                return CreateLookupResult(pluginId, "error", null, error.Message);
            }
        }
        return CreateLookupResult(pluginId, "notFound", null, null);
    }

    private static JsonNode CreateLookupResult(string pluginId, string status, JsonNode? categories, string? error)
    {
        var result = new JsonObject
        {
            ["id"] = pluginId,
            ["status"] = status,
            ["categories"] = categories?.DeepClone() ?? new JsonArray()
        };
        if (error is not null) result["error"] = error;
        return result;
    }

    private static JsonNode? FindExactMatch(JsonNode response, IReadOnlyCollection<string> identifiers)
    {
        var expected = identifiers.Select(NormalizeIdentity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return SearchRecords(response).FirstOrDefault(record => CollectIdentities(record).Any(expected.Contains));
    }

    private static IEnumerable<JsonNode> SearchRecords(JsonNode response)
    {
        if (response is JsonArray direct) return direct.Where(item => item is not null).Select(item => item!);
        if (response is not JsonObject root) return [];
        foreach (var key in new[] { "plugins", "items", "results", "packages" })
            if (root[key] is JsonArray records) return records.Where(item => item is not null).Select(item => item!);
        return [];
    }

    private static IEnumerable<string> CollectIdentities(JsonNode record)
    {
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in ObjectSources(record))
            foreach (var key in new[] { "id", "pluginId", "packageName", "installRef", "slug", "name" })
                if (ReadNodeText(source[key]) is { Length: > 0 } value) identities.Add(NormalizeIdentity(value));
        return identities;
    }

    private static IEnumerable<JsonObject> ObjectSources(JsonNode record)
    {
        var pending = new Stack<JsonNode>();
        var seen = new HashSet<JsonNode>(ReferenceEqualityComparer.Instance);
        pending.Push(record);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!seen.Add(node) || node is not JsonObject source) continue;
            yield return source;
            foreach (var key in new[] { "plugin", "package", "manifest", "metadata", "meta", "catalog", "latestVersion" })
                if (source[key] is JsonObject nested) pending.Push(nested);
        }
    }

    private static JsonNode ReadCategoryData(JsonNode record)
    {
        foreach (var source in ObjectSources(record))
            foreach (var key in new[] { "categories", "category", "capabilityCategories" })
                if (source[key] is { } categories) return categories.DeepClone();
        return new JsonArray();
    }

    private static string? ReadPluginText(JsonElement plugin, params string[] paths)
    {
        foreach (var path in paths)
            if (ReadPluginPath(plugin, path) is { } value && ReadJsonText(value) is { Length: > 0 } text) return text;
        return null;
    }

    private static JsonElement? ReadPluginPath(JsonElement plugin, string path)
    {
        var current = plugin;
        foreach (var part in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object) return null;
            var property = current.EnumerateObject().FirstOrDefault(item => string.Equals(item.Name, part, StringComparison.OrdinalIgnoreCase));
            if (property.Name is null) return null;
            current = property.Value;
        }
        return current;
    }

    private static string? ReadJsonText(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in new[] { "packageName", "name", "spec", "id" })
            if (ReadPluginText(value, key) is { Length: > 0 } text) return text;
        return null;
    }

    private static string? NormalizePackageReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var reference = value.Trim();
        foreach (var prefix in new[] { "clawhub:", "npm:" })
            if (reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) reference = reference[prefix.Length..];
        if (reference.Contains(':') || reference.StartsWith("/", StringComparison.Ordinal)) return null;
        var versionAt = reference.StartsWith('@') ? reference.IndexOf('@', reference.IndexOf('/') + 1) : reference.IndexOf('@');
        if (versionAt > 0) reference = reference[..versionAt];
        return string.IsNullOrWhiteSpace(reference) ? null : reference;
    }

    private static string NormalizeIdentity(string value) => value.Trim().TrimEnd('/').ToLowerInvariant();

    private static string? ReadNodeText(JsonNode? value)
    {
        if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text)) return text;
        if (value is JsonObject obj)
            foreach (var key in new[] { "packageName", "slug", "name", "id" })
                if (ReadNodeText(obj[key]) is { Length: > 0 } nested) return nested;
        return null;
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
