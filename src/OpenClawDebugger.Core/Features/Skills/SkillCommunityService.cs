using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenClawDebugger;

/// <summary>Validates local ClawHub skill-catalog requests.</summary>
public sealed class SkillCommunityService(IClawHubSkillClient catalog)
{
    private static readonly HashSet<string> SortModes = new(StringComparer.Ordinal)
    {
        "recommended", "downloads", "updated", "createdAt", "stars", "trending", "name"
    };

    public Task<JsonNode> BrowseAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var sort = OptionalString(payload, "sort") ?? "recommended";
        if (!SortModes.Contains(sort)) throw new InvalidDataException("技能社区排序方式无效。");
        return catalog.BrowseAsync(new ClawHubSkillBrowseRequest(
            sort, OptionalString(payload, "cursor"), OptionalBoolean(payload, "forceRefresh")), cancellationToken);
    }

    public Task<JsonNode> SearchAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var query = RequiredString(payload, "query").Trim();
        if (query.Length == 0) throw new InvalidDataException("请输入技能搜索词。");
        return catalog.SearchAsync(new ClawHubSkillSearchRequest(query, OptionalBoolean(payload, "forceRefresh")), cancellationToken);
    }

    public Task<JsonNode> ReadDetailsAsync(JsonElement payload, CancellationToken cancellationToken) =>
        catalog.ReadDetailsAsync(ValidateSlug(RequiredString(payload, "slug")),
            OptionalBoolean(payload, "forceRefresh"), cancellationToken);

    private static string ValidateSlug(string slug)
    {
        var name = slug.Trim();
        if (name.Length == 0 || name.StartsWith("-", StringComparison.Ordinal) ||
            name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('@' or '.' or '_' or '/' or '-')) ||
            name.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("ClawHub 技能名称格式无效。");
        return name;
    }

    private static string RequiredString(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("技能社区参数无效：" + name);
        return value.GetString() ?? "";
    }

    private static string? OptionalString(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException("技能社区参数无效：" + name);
        return value.GetString();
    }

    private static bool OptionalBoolean(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("技能社区参数无效：" + name);
        return value.GetBoolean();
    }
}
