using System.IO;
using System.Text.Json;

namespace OpenClawDebugger;

public sealed record SkillServiceDependencies(
    IRemoteSkillClient Remote,
    Func<ConnectionSettings> Settings,
    Func<bool> IsConnected,
    Action<bool> SetBusy);

/// <summary>校验技能管理请求，并将远端读取与更新交给技能领域客户端。</summary>
public sealed class SkillService(SkillServiceDependencies dependencies)
{
    public Task<object> GetAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(new RemoteSkillRequest("skills_inventory"), cancellationToken);

    public Task<object> InspectAsync(JsonElement payload, CancellationToken cancellationToken = default) =>
        ExecuteAsync(new RemoteSkillRequest("skills_info", SkillName(payload)), cancellationToken);

    public Task<object> UpdateAsync(JsonElement payload, CancellationToken cancellationToken = default) =>
        ExecuteAsync(new RemoteSkillRequest("skills_update", SkillName(payload)), cancellationToken);

    public Task<object> InstallCommunityAsync(JsonElement payload, CancellationToken cancellationToken = default) =>
        ExecuteAsync(new RemoteSkillRequest("skills_install", SkillName(payload)), cancellationToken);

    private async Task<object> ExecuteAsync(RemoteSkillRequest request, CancellationToken cancellationToken)
    {
        if (!dependencies.IsConnected()) throw new InvalidOperationException("请先连接服务器，再管理技能。");
        dependencies.SetBusy(true);
        try { return await dependencies.Remote.ExecuteAsync(dependencies.Settings(), request, cancellationToken); }
        finally { dependencies.SetBusy(false); }
    }

    private static string SkillName(JsonElement payload)
    {
        if (!payload.TryGetProperty("skillName", out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("技能管理参数无效：skillName");
        var name = value.GetString()?.Trim() ?? "";
        if (!IsValidSkillName(name)) throw new InvalidDataException("技能名称格式无效。");
        return name;
    }

    private static bool IsValidSkillName(string name)
    {
        if (name.Length == 0 || !IsNameStart(name[0]) || name.Split('/').Any(part => part is "." or "..")) return false;
        return name.All(character => char.IsAsciiLetterOrDigit(character) || character is '@' or '.' or '_' or '/' or '-');
    }

    private static bool IsNameStart(char value) => char.IsAsciiLetterOrDigit(value) || value == '@';
}
