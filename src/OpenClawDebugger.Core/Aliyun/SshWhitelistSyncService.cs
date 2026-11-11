using System.Net;
using System.IO;

namespace OpenClawDebugger;

public sealed class SshWhitelistSyncService
{
    private const string PermanentIpv4 = "58.247.22.221";
    private const string PermanentCidr = PermanentIpv4 + "/32";
    private const string DynamicRuleRemark = "OpenClaw Debugger dynamic SSH IPv4";
    private const string PermanentRuleRemark = "OpenClaw Debugger permanent SSH IPv4";
    private static readonly IPAddress PermanentAddress = IPAddress.Parse(PermanentIpv4);
    private readonly IPublicEgressIpProvider _ipProvider;
    private readonly ISwasFirewallApiFactory _apiFactory;

    public SshWhitelistSyncService(IPublicEgressIpProvider ipProvider, ISwasFirewallApiFactory apiFactory)
    {
        _ipProvider = ipProvider;
        _apiFactory = apiFactory;
    }

    public async Task<SshWhitelistSyncOutcome> SyncAsync(
        AliyunFirewallSettings settings,
        AliyunAccessKeyPair accessKey,
        CancellationToken cancellationToken)
    {
        ValidateConfiguration(settings, accessKey);
        var egressAddress = await _ipProvider.GetPublicIpv4Async(cancellationToken);
        var egressIpv4 = egressAddress.ToString();
        var egressCidr = egressIpv4 + "/32";
        var api = _apiFactory.Create(settings.RegionId, accessKey);
        var rules = await api.ListRulesAsync(settings.InstanceId, cancellationToken);
        var created = 0;
        if (await EnsurePermanentRuleAsync(api, settings, rules, cancellationToken)) created++;
        rules = await api.ListRulesAsync(settings.InstanceId, cancellationToken);

        var previousRule = FindManagedRule(settings, rules);
        var previousId = previousRule?.RuleId;
        var dynamicRule = egressIpv4 == PermanentIpv4
            ? null
            : previousRule ?? FindRuleForSource(rules, egressAddress) ?? FindBroadSshRule(rules);
        var managedRuleId = await EnsureDynamicRuleAsync(api, settings, dynamicRule, egressIpv4, egressCidr, cancellationToken);
        if (dynamicRule is null && managedRuleId is not null) created++;
        var updated = dynamicRule is not null &&
            (dynamicRule.SourceCidrIp != egressCidr || dynamicRule.RuleProtocol != "TCP" || dynamicRule.Remark != DynamicRuleRemark) ? 1 : 0;

        var verifiedRules = await api.ListRulesAsync(settings.InstanceId, cancellationToken);
        EnsureAllowRulesPresent(verifiedRules, egressAddress);
        var removed = await RemoveOldAndOpenRulesAsync(
            api, settings, verifiedRules, previousId, managedRuleId, cancellationToken);
        var finalRules = await api.ListRulesAsync(settings.InstanceId, cancellationToken);
        EnsureFinalRules(finalRules, egressAddress);

        return new SshWhitelistSyncOutcome(
            new SshWhitelistSyncResult(egressIpv4, PermanentIpv4, created, updated, removed),
            managedRuleId);
    }

    private static void ValidateConfiguration(AliyunFirewallSettings settings, AliyunAccessKeyPair accessKey)
    {
        AliyunFirewallSettingsValidator.ValidateTarget(settings.RegionId, settings.InstanceId);
        if (string.IsNullOrWhiteSpace(accessKey.AccessKeyId) || string.IsNullOrWhiteSpace(accessKey.AccessKeySecret))
            throw new InvalidDataException("请先保存阿里云 AccessKey 凭据。");
    }

    private static async Task<bool> EnsurePermanentRuleAsync(
        ISwasFirewallApi api,
        AliyunFirewallSettings settings,
        IReadOnlyList<SwasFirewallRule> rules,
        CancellationToken cancellationToken)
    {
        if (rules.Any(IsPermanentRule)) return false;
        await api.CreateSshRuleAsync(settings.InstanceId, PermanentCidr, PermanentRuleRemark, cancellationToken);
        return true;
    }

    private static async Task<string?> EnsureDynamicRuleAsync(
        ISwasFirewallApi api,
        AliyunFirewallSettings settings,
        SwasFirewallRule? currentRule,
        string egressIpv4,
        string egressCidr,
        CancellationToken cancellationToken)
    {
        if (egressIpv4 == PermanentIpv4) return null;
        if (currentRule is null)
            return await api.CreateSshRuleAsync(settings.InstanceId, egressCidr, DynamicRuleRemark, cancellationToken);
        if (currentRule.SourceCidrIp != egressCidr || currentRule.RuleProtocol != "TCP" || currentRule.Remark != DynamicRuleRemark)
            await api.ModifySshRuleAsync(settings.InstanceId, currentRule, egressCidr, DynamicRuleRemark, cancellationToken);
        return currentRule.RuleId;
    }

    private static bool IsManagedTarget(AliyunFirewallSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.ManagedDynamicRuleId) &&
        string.Equals(settings.ManagedRegionId, settings.RegionId, StringComparison.Ordinal) &&
        string.Equals(settings.ManagedInstanceId, settings.InstanceId, StringComparison.Ordinal);

    private static SwasFirewallRule? FindManagedRule(AliyunFirewallSettings settings, IEnumerable<SwasFirewallRule> rules)
    {
        var candidates = rules.Where(IsManagedDynamicRule);
        var storedId = IsManagedTarget(settings) ? settings.ManagedDynamicRuleId : null;
        return candidates.FirstOrDefault(rule => rule.RuleId == storedId)
            ?? candidates.FirstOrDefault();
    }

    private static bool IsManagedDynamicRule(SwasFirewallRule rule) =>
        IsAllowedSshRule(rule) && !IsPermanentRule(rule) && rule.Remark == DynamicRuleRemark;

    private static SwasFirewallRule? FindRuleForSource(IEnumerable<SwasFirewallRule> rules, IPAddress address) =>
        rules.FirstOrDefault(rule => IsAllowedSshRule(rule) && IsExactHost(rule.SourceCidrIp, address));

    private static SwasFirewallRule? FindBroadSshRule(IEnumerable<SwasFirewallRule> rules) =>
        rules.FirstOrDefault(rule => IsAllowedSshRule(rule) &&
            (rule.SourceCidrIp is "0.0.0.0/0" or "0.0.0.0"));

    private static bool IsAllowedSshRule(SwasFirewallRule rule) =>
        string.Equals(rule.Policy, "accept", StringComparison.OrdinalIgnoreCase) &&
        (rule.RuleProtocol is "TCP" or "TCP+UDP") && (rule.Port is "22" or "22/22");

    private static bool IsPermanentRule(SwasFirewallRule rule) =>
        IsAllowedSshRule(rule) && IsExactHost(rule.SourceCidrIp, PermanentAddress);

    private static bool IsExactHost(string sourceCidrIp, IPAddress address) =>
        string.Equals(sourceCidrIp, address.ToString(), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(sourceCidrIp, address + "/32", StringComparison.OrdinalIgnoreCase);

    private static void EnsureAllowRulesPresent(IEnumerable<SwasFirewallRule> rules, IPAddress egressAddress)
    {
        if (!rules.Any(IsPermanentRule) ||
            (egressAddress.ToString() != PermanentIpv4 && !rules.Any(rule =>
                IsAllowedSshRule(rule) && IsExactHost(rule.SourceCidrIp, egressAddress))))
            throw new InvalidOperationException("同步后没有验证到固定 IP 和当前出口 IP 的 SSH 白名单规则，未清理旧规则。");
    }

    private static async Task<int> RemoveOldAndOpenRulesAsync(
        ISwasFirewallApi api,
        AliyunFirewallSettings settings,
        IReadOnlyList<SwasFirewallRule> rules,
        string? previousId,
        string? managedRuleId,
        CancellationToken cancellationToken)
    {
        var cleanup = rules.Where(rule => IsAllowedSshRule(rule) && !IsPermanentRule(rule) &&
            ((rule.SourceCidrIp is "0.0.0.0/0" or "0.0.0.0") ||
             (rule.RuleId == previousId && rule.RuleId != managedRuleId)))
            .DistinctBy(rule => rule.RuleId)
            .ToArray();
        foreach (var rule in cleanup)
            await api.DeleteRuleAsync(settings.InstanceId, rule, cancellationToken);
        return cleanup.Length;
    }

    private static void EnsureFinalRules(IEnumerable<SwasFirewallRule> rules, IPAddress egressAddress)
    {
        EnsureAllowRulesPresent(rules, egressAddress);
        if (rules.Any(rule => IsAllowedSshRule(rule) && (rule.SourceCidrIp is "0.0.0.0/0" or "0.0.0.0")))
            throw new InvalidOperationException("当前 SSH 全网开放规则仍然存在；本次同步未完成。");
    }
}
