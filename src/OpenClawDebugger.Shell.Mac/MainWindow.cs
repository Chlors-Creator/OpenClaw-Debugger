using Avalonia.Controls;
using Avalonia.Threading;

namespace OpenClawDebugger.Shell;

internal sealed class MainWindow : Window
{
    private readonly NativeWebView _webView = new();
    private LocalWebUiServer? _server;

    public MainWindow()
    {
        Title = "OpenClaw Debugger";
        Width = 1280;
        Height = 840;
        MinWidth = 960;
        MinHeight = 640;
        Content = _webView;

        _webView.WebMessageReceived += OnWebMessageReceived;
        _webView.NavigationCompleted += OnNavigationCompleted;
        Opened += async (_, _) => await StartAsync();
        Closed += async (_, _) =>
        {
            if (_server is not null) await _server.DisposeAsync();
        };
    }

    private async Task StartAsync()
    {
        try
        {
            _server = LocalWebUiServer.Start(Path.Combine(AppContext.BaseDirectory, "WebUi"));
            Console.WriteLine($"[shell] 界面服务已就绪：{_server.BaseUri}");
            _webView.Source = _server.BaseUri;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[shell] 启动失败：{ex}");
            Close();
        }
    }

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        Console.WriteLine($"[bridge] 收到界面消息：{e.Body}");
    }

    private async void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        Console.WriteLine($"[shell] 页面加载完成：success={e.IsSuccess}");

        // 探针：确认渲染、资源与桥接垫片三者都到位。
        try
        {
            var probe = await _webView.InvokeScript("""
                JSON.stringify({
                  title: document.title,
                  scripts: document.scripts.length,
                  shim: !!(window.chrome && window.chrome.webview && window.chrome.webview.postMessage),
                  invoke: typeof window.invokeCSharpAction,
                  bridge: typeof window.OpenClawBridge,
                  styles: document.styleSheets.length,
                  body: document.body ? document.body.childElementCount : -1
                })
                """);
            Console.WriteLine($"[smoke] 探针结果：{probe}");

            await _webView.InvokeScript("window.chrome.webview.postMessage({ smoke: 'ping' }); true");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[smoke] 探针失败：{ex.GetType().Name} {ex.Message}");
        }

        if (Environment.GetEnvironmentVariable("OPENCLAW_SMOKE") == "1")
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Console.WriteLine("[smoke] 自动退出");
                Close();
            };
            timer.Start();
        }
    }
}
