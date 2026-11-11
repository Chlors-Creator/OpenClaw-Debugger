using System.Text.Json.Nodes;

namespace OpenClawDebugger;

public sealed record RemotePluginRequest(
    string Action,
    string? Query = null,
    string? PluginId = null,
    string? PackageSpec = null,
    bool AcceptCapabilities = false);

public interface IRemotePluginClient
{
    Task<JsonObject> ExecuteAsync(
        ConnectionSettings settings,
        RemotePluginRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>通过受限远程代理执行 OpenClaw 插件 CLI 操作。</summary>
public sealed class RemotePluginClient(RemoteOpenClawClient remote) : IRemotePluginClient
{
    public Task<JsonObject> ExecuteAsync(
        ConnectionSettings settings,
        RemotePluginRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject { ["action"] = request.Action };
        if (request.Query is not null) payload["query"] = request.Query;
        if (request.PluginId is not null) payload["pluginId"] = request.PluginId;
        if (request.PackageSpec is not null) payload["packageSpec"] = request.PackageSpec;
        if (request.Action is "plugins_enable" or "plugins_reload" or "plugins_update" or "plugins_install")
            payload["acceptCapabilities"] = request.AcceptCapabilities;
        return remote.InvokePluginAsync(settings, payload, cancellationToken);
    }
}
