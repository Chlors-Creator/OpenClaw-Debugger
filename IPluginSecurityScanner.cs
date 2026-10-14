using System.Text.Json.Nodes;

namespace OpenClawDebugger;

public sealed record PluginSecurityCheck(string PackageName, string Version, bool ForceRefresh);

/// <summary>Security report provider contract. Implementations may be replaced by a local or hosted scanner.</summary>
public interface IPluginSecurityScanner
{
    Task<JsonNode> CheckAsync(PluginSecurityCheck request, CancellationToken cancellationToken);
}

/// <summary>Reads the published ClawHub scan report; this does not claim to run a local scan.</summary>
public sealed class ClawHubPluginSecurityScanner(IClawHubPluginClient client) : IPluginSecurityScanner
{
    public Task<JsonNode> CheckAsync(PluginSecurityCheck request, CancellationToken cancellationToken) =>
        client.ReadSecurityReportAsync(
            new ClawHubSecurityReportRequest(request.PackageName, request.Version, request.ForceRefresh), cancellationToken);
}
