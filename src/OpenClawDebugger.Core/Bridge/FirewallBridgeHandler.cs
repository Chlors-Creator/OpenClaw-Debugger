using System.Text.Json;
using System.IO;

namespace OpenClawDebugger;

public sealed class FirewallBridgeHandler
{
    private readonly SettingsService _settingsService;
    private readonly Func<UserSettings> _settings;
    private readonly IAliyunAccessKeyVault _accessKeyVault;
    private readonly SshWhitelistSyncService _syncService;

    public FirewallBridgeHandler(
        SettingsService settingsService,
        Func<UserSettings> settings,
        IAliyunAccessKeyVault accessKeyVault,
        SshWhitelistSyncService syncService)
    {
        _settingsService = settingsService;
        _settings = settings;
        _accessKeyVault = accessKeyVault;
        _syncService = syncService;
    }

    public void Register(BridgeCommandRouter router)
    {
        router.Map("saveFirewallSettings", SaveSettingsAsync);
        router.Map("syncSshWhitelist", SyncSshWhitelistAsync);
    }

    private async Task<object?> SaveSettingsAsync(JsonElement payload, CancellationToken _)
    {
        var configured = await _settingsService.SaveFirewallSettingsAsync(payload, _settings(), _accessKeyVault);
        return new { settings = _settings().Firewall, aliyunCredentialsConfigured = configured };
    }

    private async Task<object?> SyncSshWhitelistAsync(JsonElement _, CancellationToken cancellationToken)
    {
        var settings = _settings();
        var accessKey = _accessKeyVault.Read()
            ?? throw new InvalidDataException("请先在设置页保存阿里云 AccessKey 凭据。");
        var outcome = await _syncService.SyncAsync(settings.Firewall, accessKey, cancellationToken);
        settings.Firewall = settings.Firewall with
        {
            ManagedDynamicRuleId = outcome.ManagedDynamicRuleId,
            ManagedRegionId = settings.Firewall.RegionId,
            ManagedInstanceId = settings.Firewall.InstanceId
        };
        await SettingsRepository.SaveAsync(settings);
        return outcome.Result;
    }
}
