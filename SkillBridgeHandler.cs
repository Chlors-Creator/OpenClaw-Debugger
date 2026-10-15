using System.Text.Json;

namespace OpenClawDebugger;

/// <summary>技能清单、详情和远程更新的 WebView 命令入口。</summary>
public sealed class SkillBridgeHandler(Func<SkillService?> service)
{
    public void Register(BridgeCommandRouter router)
    {
        router.Map("getSkills", async (_, token) => await Require().GetAsync(token));
        router.Map("inspectSkill", async (payload, token) => await Require().InspectAsync(payload, token));
        router.Map("updateSkill", async (payload, token) => await Require().UpdateAsync(payload, token));
    }

    private SkillService Require() => service() ?? throw new InvalidOperationException("技能服务未初始化。");
}
