namespace OpenClawDebugger;

public sealed record AliyunFirewallSettings
{
    public string RegionId { get; init; } = "";
    public string InstanceId { get; init; } = "";
    public bool AutoSyncOnStartup { get; init; }
    public string? ManagedDynamicRuleId { get; init; }
    public string? ManagedRegionId { get; init; }
    public string? ManagedInstanceId { get; init; }
}

public sealed class AliyunAccessKeyPair
{
    public string AccessKeyId { get; init; } = "";
    public string AccessKeySecret { get; init; } = "";
}

public sealed record SwasFirewallRule(
    string RuleId,
    string RuleProtocol,
    string Port,
    string SourceCidrIp,
    string Policy,
    string? Remark);

public sealed record SshWhitelistSyncResult(
    string EgressIpv4,
    string PermanentIpv4,
    int RulesCreated,
    int RulesUpdated,
    int RulesRemoved);

public sealed record SshWhitelistSyncOutcome(
    SshWhitelistSyncResult Result,
    string? ManagedDynamicRuleId);
