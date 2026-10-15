using System.Text.Json.Nodes;

namespace OpenClawDebugger;

public sealed record RemoteSkillRequest(string Action, string? SkillName = null);

public interface IRemoteSkillClient
{
    Task<JsonObject> ExecuteAsync(
        ConnectionSettings settings,
        RemoteSkillRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>通过受限远程代理执行 OpenClaw 技能 CLI 操作。</summary>
public sealed class RemoteSkillClient(RemoteOpenClawClient remote) : IRemoteSkillClient
{
    public Task<JsonObject> ExecuteAsync(
        ConnectionSettings settings,
        RemoteSkillRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject { ["action"] = request.Action };
        if (request.SkillName is not null) payload["skillName"] = request.SkillName;
        return remote.InvokeSkillAsync(settings, payload, cancellationToken);
    }
}
