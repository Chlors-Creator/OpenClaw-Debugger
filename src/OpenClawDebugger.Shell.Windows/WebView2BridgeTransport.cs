using Microsoft.Web.WebView2.Core;

namespace OpenClawDebugger;

/// <summary>
/// WebView2 的桥接传输实现。这是整个 Windows 壳里唯一直接调用 CoreWebView2 消息 API 的地方，
/// Core 侧只认 <see cref="IBridgeTransport"/>。
/// </summary>
public sealed class WebView2BridgeTransport(CoreWebView2 core) : IBridgeTransport
{
    public void PostMessage(string json)
    {
        try
        {
            core.PostWebMessageAsJson(json);
        }
        catch (InvalidOperationException)
        {
            // 窗口/浏览器已关闭时投递必然失败，属于正常竞态。
        }
    }
}
