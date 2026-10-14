using System.Text.Json.Nodes;

namespace OpenClawDebugger;

/// <summary>Security report provider contract. Implementations may be replaced by a local or hosted scanner.</summary>
public interface IPluginSecurityScanner
{
    Task<JsonNode> CheckAsync(string packageName, string version, bool forceRefresh, CancellationToken cancellationToken);
}

/// <summary>Reads the published ClawHub scan report; this does not claim to run a local scan.</summary>
public sealed class ClawHubPluginSecurityScanner(IClawHubPluginClient client) : IPluginSecurityScanner
{
    public Task<JsonNode> CheckAsync(string packageName, string version, bool forceRefresh, CancellationToken cancellationToken) =>
        client.ReadSecurityReportAsync(packageName, version, forceRefresh, cancellationToken);
}
