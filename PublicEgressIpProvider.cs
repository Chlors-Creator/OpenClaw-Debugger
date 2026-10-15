using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace OpenClawDebugger;

public interface IPublicEgressIpProvider
{
    Task<IPAddress> GetPublicIpv4Async(CancellationToken cancellationToken);
}

public sealed class PublicEgressIpProvider : IPublicEgressIpProvider
{
    private static readonly Uri Endpoint = new("https://api4.ipify.org/");
    private static readonly IPNetwork[] NonPublicRanges =
    [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.0.2.0/24"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("198.51.100.0/24"),
        IPNetwork.Parse("203.0.113.0/24"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4")
    ];

    private readonly HttpClient _httpClient;

    public PublicEgressIpProvider(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<IPAddress> GetPublicIpv4Async(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(Endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var value = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != AddressFamily.InterNetwork ||
            NonPublicRanges.Any(range => range.Contains(address)))
            throw new InvalidDataException("公网 IP 查询服务返回了无效的公网 IPv4 地址。");
        return address;
    }
}
