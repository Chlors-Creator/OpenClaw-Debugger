using System.Text.Json;

namespace OpenClawDebugger;

/// <summary>
/// 把桥接响应序列化后经 <see cref="IBridgeTransport"/> 投递给界面。
/// 不再直接持有 WebView2 类型，宿主关闭导致的异常由传输实现吞掉。
/// </summary>
public sealed class BridgeResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private IBridgeTransport? _transport;

    public void Attach(IBridgeTransport transport) => _transport = transport;

    public void Respond(string id, bool ok, object? data, string? error)
    {
        _transport?.PostMessage(JsonSerializer.Serialize(new BridgeResponse(id, ok, data, error), JsonOptions));
    }

    public void SendProgress(string command, object data)
    {
        _transport?.PostMessage(JsonSerializer.Serialize(new BridgeEvent("progress", command, data), JsonOptions));
    }

    private sealed record BridgeResponse(string Id, bool Ok, object? Data, string? Error);
    private sealed record BridgeEvent(string Type, string Command, object Data);
}
