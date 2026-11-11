using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OpenClawDebugger;

/// <summary>
/// 把 shared/WebUi 以回环 HTTP 方式提供給 WKWebView。
/// macOS 上无法像 WebView2 那样用虚拟主机映射，因此走 127.0.0.1 + 随机端口，
/// 保证 fetch()、相对路径与 ES 模块的行为和 Windows 侧一致。
/// </summary>
internal sealed class LocalWebUiServer : IAsyncDisposable
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf"
    };

    private readonly HttpListener _listener = new();
    private readonly string _root;
    private readonly CancellationTokenSource _shutdown = new();

    private LocalWebUiServer(string root, int port)
    {
        _root = Path.GetFullPath(root);
        BaseUri = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(BaseUri.ToString());
    }

    public Uri BaseUri { get; }

    public static LocalWebUiServer Start(string webUiDirectory)
    {
        if (!Directory.Exists(webUiDirectory))
            throw new DirectoryNotFoundException($"找不到界面资源目录：{webUiDirectory}");

        var lastError = default(Exception);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var server = new LocalWebUiServer(webUiDirectory, ReserveLoopbackPort());
            try
            {
                server._listener.Start();
                _ = Task.Run(server.AcceptLoopAsync);
                return server;
            }
            catch (HttpListenerException ex)
            {
                lastError = ex;
                server._listener.Close();
            }
        }
        throw new InvalidOperationException("无法在回环地址上启动界面服务。", lastError);
    }

    private static int ReserveLoopbackPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_shutdown.IsCancellationRequested || !_listener.IsListening)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(context));
        }
    }

    private async Task ServeAsync(HttpListenerContext context)
    {
        try
        {
            var relative = (context.Request.Url?.AbsolutePath ?? "/").TrimStart('/');
            if (relative.Length == 0) relative = "index.html";
            var candidate = Path.GetFullPath(Path.Combine(_root, Uri.UnescapeDataString(relative)));
            if (!candidate.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                context.Response.Close();
                return;
            }

            if (!File.Exists(candidate))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                return;
            }

            var bytes = await File.ReadAllBytesAsync(candidate).ConfigureAwait(false);
            context.Response.ContentType = ContentTypes.TryGetValue(Path.GetExtension(candidate), out var type)
                ? type
                : "application/octet-stream";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.Close();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[webui] 提供 {context.Request.Url} 失败：{ex.Message}");
            try { context.Response.Abort(); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        try { _listener.Stop(); } catch { }
        _listener.Close();
        _shutdown.Dispose();
    }
}
