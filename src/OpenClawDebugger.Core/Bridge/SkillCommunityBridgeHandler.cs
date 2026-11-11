namespace OpenClawDebugger;

/// <summary>Local read-only ClawHub skill catalog bridge commands.</summary>
public sealed class SkillCommunityBridgeHandler(SkillCommunityService service)
{
    public void Register(BridgeCommandRouter router)
    {
        router.Map("browseSkillCommunity", async (payload, token) => await service.BrowseAsync(payload, token));
        router.Map("searchSkillCommunity", async (payload, token) => await service.SearchAsync(payload, token));
        router.Map("getSkillCommunityDetails", async (payload, token) => await service.ReadDetailsAsync(payload, token));
    }
}
