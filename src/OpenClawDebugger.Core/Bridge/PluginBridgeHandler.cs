using System.Text.Json;

namespace OpenClawDebugger;

/// <summary>插件查询与生命周期操作的 WebView 命令入口。</summary>
public sealed class PluginBridgeHandler(Func<PluginService?> service)
{
    public void Register(BridgeCommandRouter router)
    {
        router.Map("getPlugins", async (_, token) => await Require().GetAsync(token));
        router.Map("searchPlugins", async (payload, token) => await Require().SearchAsync(payload, token));
        router.Map("inspectPlugin", async (payload, token) => await Require().InspectAsync(payload, token));
        router.Map("setPluginEnabled", async (payload, token) => await Require().SetEnabledAsync(payload, token));
        router.Map("reloadPlugin", async (payload, token) => await Require().ReloadAsync(payload, token));
        router.Map("updatePlugin", async (payload, token) => await Require().UpdateAsync(payload, token));
        router.Map("uninstallPlugin", async (payload, token) => await Require().UninstallAsync(payload, token));
        router.Map("installPlugin", async (payload, token) => await Require().InstallAsync(payload, token));
    }

    private PluginService Require() => service() ?? throw new InvalidOperationException("插件服务未初始化。");
}
