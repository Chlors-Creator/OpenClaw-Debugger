namespace OpenClawDebugger;

/// <summary>
/// 桥接传输通道。Core 只负责把 JSON 消息交给界面层，
/// 具体是 WebView2 还是 WKWebView 由各平台的壳决定。
/// </summary>
public interface IBridgeTransport
{
    /// <summary>向界面投递一条 JSON 消息。实现方需自行吞掉界面已关闭导致的异常。</summary>
    void PostMessage(string json);
}

/// <summary>界面发来的一次请求，已与具体 WebView 的消息类型解耦。</summary>
public sealed record BridgeRequestEnvelope(
    string SourceUri,
    string Json,
    IReadOnlyList<BridgeFile> Files);

/// <summary>界面上传的本地文件句柄。</summary>
public sealed record BridgeFile(string Path);
