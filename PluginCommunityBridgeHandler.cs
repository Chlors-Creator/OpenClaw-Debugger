namespace OpenClawDebugger;

/// <summary>Local read-only ClawHub catalog and security report bridge commands.</summary>
public sealed class PluginCommunityBridgeHandler(PluginCommunityService service)
{
    public void Register(BridgeCommandRouter router)
    {
        router.Map("browsePluginCommunity", async (payload, token) => await service.BrowseAsync(payload, token));
        router.Map("searchPluginCommunity", async (payload, token) => await service.SearchAsync(payload, token));
        router.Map("checkPluginCommunitySecurity", async (payload, token) => await service.CheckSecurityAsync(payload, token));
        router.Map("lookupInstalledPluginCategories", async (payload, token) => await service.LookupInstalledPluginCategoriesAsync(payload, token));
    }
}
