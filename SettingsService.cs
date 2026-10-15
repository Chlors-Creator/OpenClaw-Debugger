using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenClawDebugger;

/// <summary>应用设置的校验和持久化边界，避免窗口代码直接处理配置格式。</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task SaveConnectionAsync(JsonElement payload, UserSettings settings)
    {
        var incoming = payload.Deserialize<ConnectionSettings>(JsonOptions)
            ?? throw new InvalidDataException("设置内容无效。");
        incoming.Host = incoming.Host.Trim();
        incoming.Username = incoming.Username.Trim();
        incoming.WorkspacePath = incoming.WorkspacePath.Trim();
        incoming.StickersPath = incoming.StickersPath.Trim();
        if (incoming.Host.Length is < 1 or > 253 || incoming.Host.Any(char.IsWhiteSpace) || incoming.Host.Any(char.IsControl))
            throw new InvalidDataException("服务器地址格式无效。");
        if (!Regex.IsMatch(incoming.Username, "^[a-zA-Z0-9_.-]{1,64}$"))
            throw new InvalidDataException("SSH 用户名格式无效。");
        if (incoming.Port is < 1 or > 65535)
            throw new InvalidDataException("SSH 端口需要在 1–65535 之间。");
        ValidateRemotePath(incoming.WorkspacePath, "工作区");
        ValidateRemotePath(incoming.StickersPath, "表情包目录");
        if (!payload.TryGetProperty("backupRetentionCount", out var retention) ||
            retention.ValueKind != JsonValueKind.Number || !retention.TryGetInt32(out var retentionCount) ||
            retentionCount is < 1 or > 30)
            throw new InvalidDataException("备份保留数量需要在 1–30 之间。");
        settings.Connection = incoming;
        settings.BackupRetentionCount = retentionCount;
        settings.PrivateDirectory = SettingsRepository.DefaultPrivateDirectory;
        await SettingsRepository.SaveAsync(settings);
    }

    public async Task<bool> SaveFirewallSettingsAsync(
        JsonElement payload,
        UserSettings settings,
        IAliyunAccessKeyVault accessKeyVault)
    {
        var incoming = payload.Deserialize<FirewallSettingsSubmission>(JsonOptions)
            ?? throw new InvalidDataException("阿里云防火墙设置内容无效。");
        var regionId = incoming.RegionId.Trim();
        var instanceId = incoming.InstanceId.Trim();
        AliyunFirewallSettingsValidator.ValidateTarget(regionId, instanceId);

        var accessKeyId = incoming.AccessKeyId.Trim();
        var secretIsBlank = string.IsNullOrWhiteSpace(incoming.AccessKeySecret);
        if ((accessKeyId.Length == 0) != secretIsBlank)
            throw new InvalidDataException("更换阿里云密钥时，AccessKey ID 和 Secret 必须同时填写。");
        if (accessKeyId.Length > 256)
            throw new InvalidDataException("阿里云 AccessKey ID 长度无效。");
        if (accessKeyId.Length == 0 && !accessKeyVault.IsConfigured)
            throw new InvalidDataException("请填写阿里云 AccessKey ID 和 Secret，以便保存到 Windows 凭据管理器。");
        if (accessKeyId.Length > 0)
            accessKeyVault.Save(new AliyunAccessKeyPair
            {
                AccessKeyId = accessKeyId,
                AccessKeySecret = incoming.AccessKeySecret
            });

        var previous = settings.Firewall ?? new AliyunFirewallSettings();
        var sameTarget = string.Equals(previous.RegionId, regionId, StringComparison.Ordinal) &&
            string.Equals(previous.InstanceId, instanceId, StringComparison.Ordinal);
        settings.Firewall = new AliyunFirewallSettings
        {
            RegionId = regionId,
            InstanceId = instanceId,
            AutoSyncOnStartup = incoming.AutoSyncOnStartup,
            ManagedDynamicRuleId = sameTarget ? previous.ManagedDynamicRuleId : null,
            ManagedRegionId = sameTarget ? previous.ManagedRegionId : null,
            ManagedInstanceId = sameTarget ? previous.ManagedInstanceId : null
        };
        settings.PrivateDirectory = SettingsRepository.DefaultPrivateDirectory;
        await SettingsRepository.SaveAsync(settings);
        return accessKeyVault.IsConfigured;
    }

    public async Task<string> SetThemeAsync(JsonElement payload, UserSettings settings)
    {
        var theme = payload.GetProperty("theme").GetString() ?? "Atri";
        if (theme is not ("Atri" or "Luoxi" or "Light"))
            throw new InvalidDataException("未知主题。");
        settings.ThemeName = theme;
        await SettingsRepository.SaveAsync(settings);
        return theme;
    }

    private static void ValidateRemotePath(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') || path.Contains('\n') || path.Contains('\r') || path.Contains('\0'))
            throw new InvalidDataException(label + "路径必须是合法的绝对路径。");
    }

    private sealed class FirewallSettingsSubmission
    {
        public string RegionId { get; init; } = "";
        public string InstanceId { get; init; } = "";
        public bool AutoSyncOnStartup { get; init; }
        public string AccessKeyId { get; init; } = "";
        public string AccessKeySecret { get; init; } = "";
    }
}
