using System.Net.Http;
using System.Text.Json;
using AppKit;
using CoreGraphics;
using CoreFoundation;
using Foundation;
using WebKit;

namespace OpenClawDebugger;

/// <summary>
/// macOS 壳主窗口：纯 AppKit 的 NSWindow + WKWebView。
/// 职责只有三件：开窗口、托管 WKWebView、装配 Core 的服务并用桥接把两边连起来。
/// 标题栏、红绿灯、圆角、拖动全部由原生 NSWindow 提供。
/// </summary>
internal sealed class MainWindow : NSWindow, IWKScriptMessageHandler
{
    private const string ScriptMessageHandlerName = "openclawHost";

    private readonly BridgeResponseWriter _bridgeResponses = new();
    private readonly SettingsService _settingsService = new();
    private readonly HttpClient _clawHubHttpClient;
    private readonly HttpClient _egressIpHttpClient;
    private readonly IAliyunAccessKeyVault _aliyunAccessKeyVault;
    private readonly SshWhitelistSyncService _sshWhitelistSync;
    private readonly PluginCommunityService _pluginCommunity;
    private readonly SkillCommunityService _skillCommunity;
    private readonly RemoteTaskScheduler _taskScheduler;
    private readonly CustomArtifactService _customArtifacts;
    private readonly ConnectionCoordinator _connection;
    private readonly WKWebView _webView;
    private readonly object _busySync = new();

    private CustomArtifactBridgeHandler? _customArtifactBridge;
    private BridgeDispatcher? _bridgeDispatcher;
    private BridgeCommandRouter? _commandRouter;
    private BridgeCommandContract? _bridgeContract;
    private LocalWebUiServer? _server;
    private UserSettings _settings = new();
    private bool _busy;
    private int _busyCount;
    private bool _hasDrafts;
    private bool _closingApproved;
    private bool _initialized;
    private bool _probed;

    public MainWindow()
        : base(
            new CGRect(0, 0, 1280, 840),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable |
            NSWindowStyle.Resizable | NSWindowStyle.FullSizeContentView,
            NSBackingStore.Buffered,
            false)
    {
        Title = "OpenClaw Debugger";
        MinSize = new CGSize(960, 640);

        // 原生无标题栏：标题栏透明 + 隐藏标题文字，但保留红绿灯、圆角与标题栏高度/拖动区。
        TitlebarAppearsTransparent = true;
        TitleVisibility = NSWindowTitleVisibility.Hidden;
        MovableByWindowBackground = true;

        _webView = CreateWebView();

        _clawHubHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        _egressIpHttpClient = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        var credentialStore = new MacKeychainCredentialStore();
        _aliyunAccessKeyVault = new CredentialAliyunAccessKeyVault(credentialStore);
        SshExecutableLocator.Current = new UnixSshLocator();
        _sshWhitelistSync = new SshWhitelistSyncService(
            new PublicEgressIpProvider(_egressIpHttpClient), new SwasFirewallApiFactory());
        var clawHubClient = new ClawHubPluginClient(_clawHubHttpClient);
        _pluginCommunity = new PluginCommunityService(clawHubClient, new ClawHubPluginSecurityScanner(clawHubClient));
        _skillCommunity = new SkillCommunityService(new ClawHubSkillClient(_clawHubHttpClient));
        _customArtifacts = new CustomArtifactService(SettingsRepository.DefaultPrivateDirectory);
        _taskScheduler = new RemoteTaskScheduler(status => _bridgeResponses.SendProgress("task", status));
        _connection = new ConnectionCoordinator(
            () => _busy,
            SetBusy,
            (command, data) => _bridgeResponses.SendProgress(command, data),
            credentialStore,
            new MacSecretProtector(credentialStore),
            new SipsThumbnailEncoder());

        ContentView = _webView;

        // 窗口即将关闭时清理资源（等价于原 Avalonia 的 Closing）。
        NSNotificationCenter.DefaultCenter.AddObserver(NSWindow.WillCloseNotification, _ => Cleanup(), this);

        // 初始化在窗口首次显示前执行（等价于原 Avalonia 的 Opened）。
        _ = InitializeAsync();
    }

    private WKWebView CreateWebView()
    {
        var configuration = new WKWebViewConfiguration();
        configuration.UserContentController.AddScriptMessageHandler(this, ScriptMessageHandlerName);
        configuration.UserContentController.AddUserScript(new WKUserScript(
            new NSString("document.documentElement.classList.add('mac-window');"),
            WKUserScriptInjectionTime.AtDocumentStart,
            false));

        var webView = new WKWebView(CGRect.Empty, configuration);
        webView.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        webView.NavigationDelegate = new NavigationDelegate(this);
        return webView;
    }

    /// <summary>IWKScriptMessageHandler：接收前端 window.webkit.messageHandlers.openclawHost.postMessage(...)。</summary>
    public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
    {
        if (message.Name != ScriptMessageHandlerName) return;
        if (message.Body is not NSString body) return;

        var dispatcher = _bridgeDispatcher;
        if (dispatcher is null || _server is null) return;

        var text = body.ToString();
        if (string.IsNullOrEmpty(text)) return;
        if (Environment.GetEnvironmentVariable("OPENCLAW_BRIDGE_LOG") == "1")
            Console.WriteLine($"[bridge←ui] {(text.Length <= 300 ? text : text[..300] + "…")}");

        // WKWebView 的消息不带来源地址，由壳声明自己的来源；
        // 越界导航由 NavigationDelegate 兜回，保证只有本地页面能持有这条通道。
        var envelope = new BridgeRequestEnvelope(_server.BaseUri.ToString(), text, []);
        _ = dispatcher.HandleAsync(envelope);
    }

    private async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            _settings = await SettingsRepository.LoadAsync();
            _settings.PrivateDirectory = SettingsRepository.DefaultPrivateDirectory;
            OperationLogStore.Configure(_settings.PrivateDirectory);
            if (_settings.ThemeName is not ("Atri" or "Luoxi" or "Light")) _settings.ThemeName = "Atri";
            _connection.Initialize(_settings);

            _commandRouter = CreateCommandRouter();
            var webUiDir = ResolveWebUiDirectory();
            _bridgeContract = BridgeCommandContract.Load(
                Path.Combine(webUiDir, "bridge-contract.json"));
            _bridgeContract.ValidateRegisteredCommands(_commandRouter.Commands);

            _server = LocalWebUiServer.Start(webUiDir);
            _bridgeResponses.Attach(new NativeBridgeTransport(_webView));
            _bridgeDispatcher = new BridgeDispatcher(
                DispatchAsync,
                _bridgeResponses,
                _bridgeContract,
                [_server.BaseUri.ToString()]);

            // 每次启动换一个查询串，避免 WKWebView 复用旧脚本缓存。
            var cacheKey = File.GetLastWriteTimeUtc(typeof(MainWindow).Assembly.Location).Ticks;
            var url = new Uri(_server.BaseUri, "index.html?v=" + cacheKey.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Console.WriteLine($"[shell] 界面服务已就绪：{_server.BaseUri}");
            // WKWebView 加载必须主线程。
            DispatchQueue.MainQueue.DispatchAsync(() =>
                _webView.LoadRequest(new NSUrlRequest(new NSUrl(url.ToString()))));
            StartSelfTestIfRequested();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[shell] 初始化失败：{ex}");
            Close();
        }
    }

    /// <summary>
    /// 诊断开关：设置 OPENCLAW_SELFTEST_PATH 后，应用启动会自动连接一次并把结果写到该文件。
    /// 用于在没有图形交互的环境里验证连接链路和本机网关自动启动。
    /// </summary>
    private void StartSelfTestIfRequested()
    {
        var path = Environment.GetEnvironmentVariable("OPENCLAW_SELFTEST_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;

        _ = Task.Run(async () =>
        {
            var lines = new List<string>();
            try
            {
                var result = await ConnectAsync(CancellationToken.None);
                lines.Add(JsonSerializer.Serialize(result));
            }
            catch (Exception ex)
            {
                lines.Add("CONNECT-FAILED " + ex.Message);
            }

            var port = LocalGatewayLauncher.ResolvePort();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(240) &&
                   !await LocalGatewayLauncher.IsListeningAsync(port, CancellationToken.None))
                await Task.Delay(1000);
            var listening = await LocalGatewayLauncher.IsListeningAsync(port, CancellationToken.None);
            lines.Add($"gatewayPort={port} listening={listening} waited={watch.Elapsed.TotalSeconds:F0}s");
            await File.WriteAllLinesAsync(path, lines);
        });
    }

    private async Task<object?> DispatchAsync(
        string operationId, string command, JsonElement payload,
        IReadOnlyList<BridgeFile> files, CancellationToken cancellationToken)
    {
        var contract = _bridgeContract ?? throw new InvalidOperationException("界面协议尚未加载。");
        var definition = contract.GetCommand(command);
        var router = _commandRouter ?? throw new InvalidOperationException("界面命令路由器未初始化。");

        if (command == "uploadCustomArtifact")
        {
            var path = files.FirstOrDefault()?.Path ?? throw new InvalidDataException("没有收到本地 ZIP 包。");
            return await (_customArtifactBridge ?? throw new InvalidOperationException("自定义上传服务未初始化。"))
                .UploadFileAsync(payload, path, cancellationToken);
        }

        // 本地界面操作与显式中断不占用 SSH 队列。
        var immediate = definition.Mode is "local" ||
            command is "toggleBackupPause" or "cancelBackup" or "cancelStickerUpload" or "close";
        if (immediate)
        {
            var result = await router.DispatchAsync(command, payload, cancellationToken);
            if (command == "toggleBackupPause")
                _taskScheduler.ReportActive(_connection.Backup?.CurrentProgress?.Phase == "paused"
                    ? RemoteTaskState.Paused : RemoteTaskState.Running);
            else if (command == "cancelBackup")
                _taskScheduler.ReportActive(RemoteTaskState.Cancelling, "正在取消备份…");
            return result;
        }

        return await _taskScheduler.EnqueueAsync(
            operationId,
            command,
            async token => await DispatchQueuedAsync(command, payload, files, router, token),
            cancellationToken);
    }

    private async Task<object?> DispatchQueuedAsync(
        string command, JsonElement payload, IReadOnlyList<BridgeFile> files,
        BridgeCommandRouter router, CancellationToken cancellationToken)
    {
        var localPath = files.FirstOrDefault()?.Path;
        if (command == "streamStickerUpload")
            return await (_connection.Stickers ?? throw new InvalidOperationException("表情包服务未初始化。"))
                .UploadFileAsync(payload, localPath ?? throw new InvalidDataException("没有收到本地上传文件。"), cancellationToken);
        return await router.DispatchAsync(command, payload, cancellationToken);
    }

    /// <summary>
    /// 定位 WebUi 资源目录。在 .app bundle 里位于 Contents/Resources/WebUi，
    /// 直接跑（dotnet run）时位于 AppContext.BaseDirectory/WebUi。
    /// </summary>
    private static string ResolveWebUiDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        var direct = Path.Combine(baseDir, "WebUi");
        if (Directory.Exists(direct)) return direct;

        // .app bundle：Contents/MacOS 的上上级是 Contents，其下 Resources/WebUi。
        var resources = Path.Combine(baseDir, "..", "Resources", "WebUi");
        var full = Path.GetFullPath(resources);
        if (Directory.Exists(full)) return full;

        return direct; // 最后回退，让后续代码抛出具象错误。
    }

    private BridgeCommandRouter CreateCommandRouter()
    {
        var router = new BridgeCommandRouter();
        new ConnectionBridgeHandler(
            _settingsService,
            () => _settings,
            () => _connection.IsConnected,
            ConnectAsync,
            DisconnectAsync,
            _aliyunAccessKeyVault).Register(router);
        new FirewallBridgeHandler(_settingsService, () => _settings, _aliyunAccessKeyVault, _sshWhitelistSync).Register(router);
        new MemoryBridgeHandler(() => _connection.Memory).Register(router);
        new StickerBridgeHandler(() => _connection.Stickers).Register(router);
        new ModelBridgeHandler(() => _connection.Models).Register(router);
        new PluginBridgeHandler(() => _connection.Plugins).Register(router);
        new SkillBridgeHandler(() => _connection.Skills).Register(router);
        _customArtifactBridge = new CustomArtifactBridgeHandler(_customArtifacts);
        _customArtifactBridge.Register(router);
        new SkillCommunityBridgeHandler(_skillCommunity).Register(router);
        new PluginCommunityBridgeHandler(_pluginCommunity).Register(router);
        new BackupBridgeHandler(() => _connection.Backup, () => _settings.Connection, OpenFolder).Register(router);
        new WindowBridgeHandler(
            () => _settings.PrivateDirectory,
            dirty => _hasDrafts = dirty,
            () =>
            {
                _closingApproved = true;
                Close();
            },
            OpenFolder).Register(router);
        new LoggingBridgeHandler().Register(router);
        return router;
    }

    private static readonly IShellLauncher ShellLauncher = new FinderShellLauncher();

    private static void OpenFolder(string path) => ShellLauncher.OpenFolder(path);

    private Task<object> ConnectAsync(CancellationToken cancellationToken) => _connection.ConnectAsync(_settings, cancellationToken);

    private Task<object> DisconnectAsync(CancellationToken cancellationToken) => _connection.DisconnectAsync(cancellationToken);

    private void SetBusy(bool busy)
    {
        lock (_busySync)
        {
            _busyCount = busy ? _busyCount + 1 : Math.Max(0, _busyCount - 1);
            _busy = _busyCount > 0;
        }
        _bridgeResponses.SendProgress("busy", new { busy = _busy });
    }

    private void Cleanup()
    {
        // macOS 上没有阻塞式确认框，未保存草稿改为记日志后放行，避免关不掉窗口。
        if (!_closingApproved && _hasDrafts)
            Console.WriteLine("[shell] 关闭时存在未保存的草稿修改");

        _bridgeDispatcher?.CancelAll();
        _taskScheduler.Dispose();
        _connection.Dispose();
        _clawHubHttpClient.Dispose();
        _egressIpHttpClient.Dispose();
        if (_server is not null) _ = _server.DisposeAsync();
    }

    /// <summary>WKNavigationDelegate：页面加载完成后做越界导航兜底与界面就绪检查。</summary>
    private sealed class NavigationDelegate(MainWindow owner) : WKNavigationDelegate
    {
        public override void DidFinishNavigation(WKWebView webView, WKNavigation? navigation)
        {
            _ = ProbeAsync(webView);
        }

        private async Task ProbeAsync(WKWebView webView)
        {
            var server = owner._server;
            if (server is null) return;

            try
            {
                // WKWebView API 必须主线程调用，这里显式切回主线程再执行。
                var href = await EvaluateOnMainThreadAsync(webView, "location.href");
                var hrefText = href?.ToString() ?? "";
                if (string.IsNullOrEmpty(hrefText) || !hrefText.Contains(server.BaseUri.Authority, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"[shell] 拦截越界导航：{hrefText}");
                    DispatchQueue.MainQueue.DispatchAsync(() =>
                        webView.LoadRequest(new NSUrlRequest(new NSUrl(server.BaseUri.ToString()))));
                    return;
                }

                if (owner._probed) return;
                owner._probed = true;

                var probe = await EvaluateOnMainThreadAsync(webView, """
                    JSON.stringify({
                      title: document.title,
                      shim: !!(window.chrome && window.chrome.webview),
                      bridge: typeof window.OpenClawBridge,
                      body: document.body ? document.body.childElementCount : -1,
                      status: (document.querySelector('#statusText') || {}).textContent || '',
                      connection: (document.querySelector('#connectionStatus') || {}).textContent || '',
                      memory: (document.querySelector('#memoryCount') || {}).textContent || '',
                      stickers: (document.querySelector('#stickerCount') || {}).textContent || '',
                      tabs: document.querySelectorAll('[role=tab], .tab').length,
                      toast: (document.querySelector('#toast') || {}).textContent || '',
                      bootError: document.body.dataset.bootError || '',
                      height: document.body.scrollHeight,
                      background: getComputedStyle(document.body).backgroundColor,
                      font: getComputedStyle(document.body).fontFamily.slice(0, 40)
                    })
                    """);
                Console.WriteLine($"[shell] 界面就绪：{probe}");

                if (Environment.GetEnvironmentVariable("OPENCLAW_SMOKE") == "1")
                    _ = Task.Delay(6000).ContinueWith(_ => DispatchQueue.MainQueue.DispatchAsync(owner.Close));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[shell] 页面就绪检查失败：{ex.Message}");
            }
        }

        private static Task<NSObject> EvaluateOnMainThreadAsync(WKWebView webView, string script)
        {
            var tcs = new TaskCompletionSource<NSObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            DispatchQueue.MainQueue.DispatchAsync(async () =>
            {
                try
                {
                    var result = await webView.EvaluateJavaScriptAsync(script);
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }
    }
}
