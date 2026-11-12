using CoreFoundation;
using Foundation;
using WebKit;

namespace OpenClawDebugger;

/// <summary>
/// WKWebView 的桥接传输实现。macOS 侧唯一直接调用 WKWebView 脚本 API 的地方，
/// Core 只认 <see cref="IBridgeTransport"/>。
/// 页面里的 <c>window.__openclawHostDeliver</c> 由 host-shim.js 提供。
/// 注意：WKWebView 的 API 只能在主线程调用，所有投递都先切回主线程。
/// </summary>
public sealed class NativeBridgeTransport(WKWebView webView) : IBridgeTransport
{
    private static readonly bool Verbose =
        Environment.GetEnvironmentVariable("OPENCLAW_BRIDGE_LOG") == "1";

    public void PostMessage(string json)
    {
        if (Verbose) Console.WriteLine($"[bridge→ui] {Trim(json)}");

        // 反序列化端在页面里，这里把响应直接作为对象字面量传进去。
        var script = "window.__openclawHostDeliver(" + Escape(json) + ");";

        // WKWebView API 必须主线程调用，桥接响应常从后台线程触发，这里强制切回主线程。
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            try
            {
                _ = webView.EvaluateJavaScriptAsync(script);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                // 窗口或 WebView 已销毁时投递必然失败，属于正常竞态。
            }
        });
    }

    /// <summary>JSON 已经是合法的 JS 对象字面量，只需转义会把脚本截断的行分隔符。</summary>
    private static string Escape(string json) =>
        json.Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");

    private static string Trim(string json) =>
        json.Length <= 300 ? json : json[..300] + "…";
}
