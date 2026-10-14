using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace OpenClawDebugger;

public sealed record ClawHubBrowseRequest(string Sort, string? Cursor, bool ForceRefresh);
public sealed record ClawHubSearchRequest(string Query, bool ForceRefresh);
public sealed record ClawHubSecurityReportRequest(string PackageName, string Version, bool ForceRefresh);

public interface IClawHubPluginClient
{
    Task<JsonNode> BrowseAsync(ClawHubBrowseRequest request, CancellationToken cancellationToken);
    Task<JsonNode> SearchAsync(ClawHubSearchRequest request, CancellationToken cancellationToken);
    Task<JsonNode> ReadSecurityReportAsync(ClawHubSecurityReportRequest request, CancellationToken cancellationToken);
}

/// <summary>ClawHub public plugin catalog API. Responses are cached briefly to avoid repeated catalog reads.</summary>
public sealed class ClawHubPluginClient(HttpClient httpClient) : IClawHubPluginClient
{
    private const int BrowsePageSize = 24;
    private static readonly Uri ApiBase = new("https://clawhub.ai/api/v1/");
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public Task<JsonNode> BrowseAsync(ClawHubBrowseRequest request, CancellationToken cancellationToken)
    {
        var path = $"plugins?limit={BrowsePageSize}&sort={Uri.EscapeDataString(request.Sort)}";
        if (!string.IsNullOrWhiteSpace(request.Cursor)) path += "&cursor=" + Uri.EscapeDataString(request.Cursor);
        return GetJsonAsync(path, request.ForceRefresh, cancellationToken);
    }

    public Task<JsonNode> SearchAsync(ClawHubSearchRequest request, CancellationToken cancellationToken) =>
        GetJsonAsync("plugins/search?q=" + Uri.EscapeDataString(request.Query), request.ForceRefresh, cancellationToken);

    public Task<JsonNode> ReadSecurityReportAsync(ClawHubSecurityReportRequest request, CancellationToken cancellationToken)
    {
        var package = Uri.EscapeDataString(request.PackageName);
        var release = Uri.EscapeDataString(request.Version);
        return GetJsonAsync($"packages/{package}/versions/{release}/security", request.ForceRefresh, cancellationToken);
    }

    private async Task<JsonNode> GetJsonAsync(string path, bool forceRefresh, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!forceRefresh && _cache.TryGetValue(path, out var cached) && cached.ExpiresAt > now)
            return ParseJson(cached.Content);

        using var response = await httpClient.GetAsync(new Uri(ApiBase, path), cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw CreateRateLimitError(response, content);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"ClawHub 请求失败 (HTTP {(int)response.StatusCode}): {content.Trim()}",
                null,
                response.StatusCode);

        PruneExpiredCache(now);
        _cache[path] = new CacheEntry(content, now + CacheLifetime);
        return ParseJson(content);
    }

    private static JsonNode ParseJson(string content) =>
        JsonNode.Parse(content) ?? throw new InvalidDataException("ClawHub 返回了空的 JSON 响应。");

    private static HttpRequestException CreateRateLimitError(HttpResponseMessage response, string content)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        var retryText = retryAfter is null ? "请稍后重试。" : $"请在 {Math.Ceiling(retryAfter.Value.TotalSeconds)} 秒后重试。";
        var detail = string.IsNullOrWhiteSpace(content) ? "" : " " + content.Trim();
        return new HttpRequestException("ClawHub 请求频率受限；" + retryText + detail, null, response.StatusCode);
    }

    private void PruneExpiredCache(DateTimeOffset now)
    {
        foreach (var entry in _cache)
            if (entry.Value.ExpiresAt <= now) _cache.TryRemove(entry.Key, out _);
    }

    private sealed record CacheEntry(string Content, DateTimeOffset ExpiresAt);
}
