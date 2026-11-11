using Avalonia.Controls;
using Avalonia.Threading;

namespace OpenClawDebugger;

/// <summary>
/// WKWebView 的桥接传输实现。macOS 侧唯一直接调用 NativeWebView 脚本 API 的地方，
/// Core 只认 <see cref="IBridgeTransport"/>。
/// 页面里的 <c>window.__openclawHostDeliver</c> 由 host-shim.js 提供。
/// </summary>
public sealed class AvaloniaBridgeTransport(NativeWebView webView) : IBridgeTransport
{
    private static readonly bool Verbose =
        Environment.GetEnvironmentVariable("OPENCLAW_BRIDGE_LOG") == "1";

    public void PostMessage(string json)
    {
        if (Verbose) Console.WriteLine($"[bridge→ui] {Trim(json)}");

        // 反序列化端在页面里，这里把响应直接作为对象字面量传进去。
        var script = "window.__openclawHostDeliver(" + Escape(json) + ");";

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => PostMessage(json));
            return;
        }

        try
        {
            _ = webView.InvokeScript(script);
        }
        catch (InvalidOperationException)
        {
            // 窗口或 WebView 已销毁时投递必然失败（含 ObjectDisposedException），属于正常竞态。
        }
    }

    /// <summary>JSON 已经是合法的 JS 对象字面量，只需转义会把脚本截断的行分隔符。</summary>
    private static string Escape(string json) =>
        json.Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");

    private static string Trim(string json) =>
        json.Length <= 300 ? json : json[..300] + "…";
}
