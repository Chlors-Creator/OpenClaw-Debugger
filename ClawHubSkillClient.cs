using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace OpenClawDebugger;

public sealed record ClawHubSkillBrowseRequest(string Sort, string? Cursor, bool ForceRefresh);
public sealed record ClawHubSkillSearchRequest(string Query, bool ForceRefresh);

public interface IClawHubSkillClient
{
    Task<JsonNode> BrowseAsync(ClawHubSkillBrowseRequest request, CancellationToken cancellationToken);
    Task<JsonNode> SearchAsync(ClawHubSkillSearchRequest request, CancellationToken cancellationToken);
    Task<JsonNode> ReadDetailsAsync(string slug, bool forceRefresh, CancellationToken cancellationToken);
}

/// <summary>Reads ClawHub's public skill catalog and caches responses briefly.</summary>
public sealed class ClawHubSkillClient(HttpClient httpClient) : IClawHubSkillClient
{
    private const int BrowsePageSize = 24;
    private static readonly Uri ApiBase = new("https://clawhub.ai/api/v1/");
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public Task<JsonNode> BrowseAsync(ClawHubSkillBrowseRequest request, CancellationToken cancellationToken)
    {
        var path = $"skills?limit={BrowsePageSize}&sort={Uri.EscapeDataString(request.Sort)}";
        if (!string.IsNullOrWhiteSpace(request.Cursor)) path += "&cursor=" + Uri.EscapeDataString(request.Cursor);
        return GetJsonAsync(path, request.ForceRefresh, cancellationToken);
    }

    public Task<JsonNode> SearchAsync(ClawHubSkillSearchRequest request, CancellationToken cancellationToken) =>
        GetJsonAsync("search?q=" + Uri.EscapeDataString(request.Query), request.ForceRefresh, cancellationToken);

    public Task<JsonNode> ReadDetailsAsync(string slug, bool forceRefresh, CancellationToken cancellationToken) =>
        GetJsonAsync("skills/" + Uri.EscapeDataString(slug), forceRefresh, cancellationToken);

    private async Task<JsonNode> GetJsonAsync(string path, bool forceRefresh, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!forceRefresh && _cache.TryGetValue(path, out var cached) && cached.ExpiresAt > now)
            return ParseJson(cached.Content);

        using var response = await httpClient.GetAsync(new Uri(ApiBase, path), cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw RateLimitError(response, content);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"ClawHub 请求失败 (HTTP {(int)response.StatusCode}): {content.Trim()}", null, response.StatusCode);

        foreach (var entry in _cache)
            if (entry.Value.ExpiresAt <= now) _cache.TryRemove(entry.Key, out _);
        _cache[path] = new CacheEntry(content, now + CacheLifetime);
        return ParseJson(content);
    }

    private static JsonNode ParseJson(string content) =>
        JsonNode.Parse(content) ?? throw new InvalidDataException("ClawHub 返回了空的 JSON 响应。");

    private static HttpRequestException RateLimitError(HttpResponseMessage response, string content)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        var delay = retryAfter is null ? "请稍后重试。" : $"请在 {Math.Ceiling(retryAfter.Value.TotalSeconds)} 秒后重试。";
        var detail = string.IsNullOrWhiteSpace(content) ? "" : " " + content.Trim();
        return new HttpRequestException("ClawHub 请求频率受限；" + delay + detail, null, response.StatusCode);
    }

    private sealed record CacheEntry(string Content, DateTimeOffset ExpiresAt);
}
