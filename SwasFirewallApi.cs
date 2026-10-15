using AlibabaCloud.OpenApiClient.Models;
using AlibabaCloud.SDK.SWAS_OPEN20200601;
using AlibabaCloud.SDK.SWAS_OPEN20200601.Models;
using System.IO;

namespace OpenClawDebugger;

public interface ISwasFirewallApi
{
    Task<IReadOnlyList<SwasFirewallRule>> ListRulesAsync(string instanceId, CancellationToken cancellationToken);
    Task<string> CreateSshRuleAsync(string instanceId, string sourceCidrIp, string remark, CancellationToken cancellationToken);
    Task ModifySshRuleAsync(string instanceId, SwasFirewallRule rule, string sourceCidrIp, string remark, CancellationToken cancellationToken);
    Task DeleteRuleAsync(string instanceId, SwasFirewallRule rule, CancellationToken cancellationToken);
}

public interface ISwasFirewallApiFactory
{
    ISwasFirewallApi Create(string regionId, AliyunAccessKeyPair accessKey);
}

public sealed class SwasFirewallApiFactory : ISwasFirewallApiFactory
{
    public ISwasFirewallApi Create(string regionId, AliyunAccessKeyPair accessKey)
    {
        var config = new Config
        {
            AccessKeyId = accessKey.AccessKeyId,
            AccessKeySecret = accessKey.AccessKeySecret,
            RegionId = regionId,
            Endpoint = $"swas.{regionId}.aliyuncs.com",
            ConnectTimeout = 15_000,
            ReadTimeout = 30_000
        };
        return new SwasFirewallApi(new Client(config), regionId);
    }
}

internal sealed class SwasFirewallApi : ISwasFirewallApi
{
    private const int PageSize = 100;
    private readonly Client _client;
    private readonly string _regionId;

    public SwasFirewallApi(Client client, string regionId)
    {
        _client = client;
        _regionId = regionId;
    }

    public async Task<IReadOnlyList<SwasFirewallRule>> ListRulesAsync(string instanceId, CancellationToken cancellationToken)
    {
        var rules = new List<SwasFirewallRule>();
        for (var pageNumber = 1; ; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await _client.ListFirewallRulesAsync(new ListFirewallRulesRequest
            {
                RegionId = _regionId,
                InstanceId = instanceId,
                PageNumber = pageNumber,
                PageSize = PageSize
            });
            var body = response.Body ?? throw new InvalidDataException("阿里云未返回防火墙规则列表。");
            var page = body.FirewallRules ?? [];
            rules.AddRange(page.Select(rule => new SwasFirewallRule(
                rule.RuleId ?? "",
                rule.RuleProtocol ?? "",
                rule.Port ?? "",
                rule.SourceCidrIp ?? "",
                rule.Policy ?? "",
                rule.Remark)));
            if ((body.TotalCount.HasValue && rules.Count >= body.TotalCount.Value) || page.Count < PageSize)
                return rules;
        }
    }

    public async Task<string> CreateSshRuleAsync(string instanceId, string sourceCidrIp, string remark, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = await _client.CreateFirewallRulesAsync(new CreateFirewallRulesRequest
        {
            RegionId = _regionId,
            InstanceId = instanceId,
            ClientToken = Guid.NewGuid().ToString("D"),
            FirewallRules =
            [
                new CreateFirewallRulesRequest.CreateFirewallRulesRequestFirewallRules
                {
                    RuleProtocol = "TCP",
                    Port = "22/22",
                    SourceCidrIp = sourceCidrIp,
                    Remark = remark
                }
            ]
        });
        return response.Body?.FirewallRuleIds?.FirstOrDefault()
            ?? throw new InvalidDataException("阿里云未返回新建防火墙规则 ID。");
    }

    public async Task ModifySshRuleAsync(string instanceId, SwasFirewallRule rule, string sourceCidrIp, string remark, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _client.ModifyFirewallRuleAsync(new ModifyFirewallRuleRequest
        {
            RegionId = _regionId,
            InstanceId = instanceId,
            RuleId = rule.RuleId,
            RuleProtocol = "TCP",
            Port = "22/22",
            SourceCidrIp = sourceCidrIp,
            Remark = remark
        });
    }

    public async Task DeleteRuleAsync(string instanceId, SwasFirewallRule rule, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _client.DeleteFirewallRuleAsync(new DeleteFirewallRuleRequest
        {
            RegionId = _regionId,
            InstanceId = instanceId,
            RuleId = rule.RuleId,
            ClientToken = Guid.NewGuid().ToString("D")
        });
    }
}
