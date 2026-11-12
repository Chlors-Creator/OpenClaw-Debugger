using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;

namespace OpenClawDebugger;

/// <summary>
/// macOS 壳。职责只有三件：开窗口、托管 WKWebView、装配 Core 的服务并用桥接把两边连起来。
/// 组合顺序与 Windows 壳保持一致，便于两边对照维护。
/// </summary>
internal sealed class MainWindow : Window
{
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
    private readonly NativeWebView _webView = new();
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
    {
        Title = "OpenClaw Debugger";
        Width = 1280;
        Height = 840;
        MinWidth = 960;
        MinHeight = 640;

        // 无标题栏但保留原生红绿灯 + 圆角：让内容区延伸到系统标题栏区域。
        // 注意这里刻意「不」设 WindowDecorations.None —— 那会连红绿灯一起干掉。
        // 只设 ExtendClientAreaToDecorationsHint，配合 Opened 里的原生 titlebarAppearsTransparent。
        ExtendClientAreaToDecorationsHint = true;

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

        Content = _webView;
        _webView.WebMessageReceived += OnWebMessageReceived;
        _webView.NewWindowRequested += (_, e) => e.Handled = true;
        Opened += async (_, _) =>
        {
            ApplyFramelessNativeBehavior();
            await InitializeAsync();
        };
        Closing += OnClosing;
    }

    /// <summary>
    /// 无标题栏（保留红绿灯 + 圆角）的原生行为：
    /// 让内容铺满整个窗口，标题栏透明，但保留 macOS 原生的红黄绿按钮与窗口圆角。
    /// 仅在 macOS 上生效，其它平台为空操作。
    /// </summary>
    private void ApplyFramelessNativeBehavior()
    {
        if (!OperatingSystem.IsMacOS()) return;

        try
        {
            // 通过 NSApp.keyWindow 拿到当前窗口的原生 NSWindow 指针，
            // 避免依赖 Avalonia 未公开的原生句柄接口。
            var nsApp = ObjCRuntime.objc_msgSend(ObjCRuntime.objc_getClass("NSApplication"), Sel("sharedApplication"));
            var nsWindow = ObjCRuntime.objc_msgSend(nsApp, Sel("keyWindow"));
            if (nsWindow == IntPtr.Zero) return;

            // titlebarAppearsTransparent：标题栏透明（标题文字消失，但红绿灯保留）
            ObjCRuntime.objc_msgSend_bool(nsWindow, Sel("setTitlebarAppearsTransparent:"), true);
            // titleVisibility = hidden：隐藏标题文字（等价 NSWindowTitleHidden）
            ObjCRuntime.objc_msgSend_long(nsWindow, Sel("setTitleVisibility:"), 1);
            // fullSizeContentView：内容区铺满整窗（含标题栏下方）
            var styleMask = ObjCRuntime.objc_msgSend_ret(nsWindow, Sel("styleMask"));
            ObjCRuntime.objc_msgSend_ulong(nsWindow, Sel("setStyleMask:"),
                (ulong)(styleMask | NSWindowStyleMaskFullSizeContentView));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[shell] 应用无标题栏原生行为失败：{ex.Message}");
        }
    }

    // NSWindowStyleMaskFullSizeContentView = 1 << 15
    private const nint NSWindowStyleMaskFullSizeContentView = 1 << 15;

    private static IntPtr Sel(string name) => ObjCRuntime.sel_registerName(name);

    private static class ObjCRuntime
    {
        private const string ObjCLib = "/usr/lib/libobjc.A.dylib";

        [DllImport(ObjCLib, EntryPoint = "objc_getClass")]
        public static extern IntPtr objc_getClass(string name);

        [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr objc_msgSend_bool(IntPtr receiver, IntPtr selector, bool value);

        [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
        public static extern nint objc_msgSend_ret(IntPtr receiver, IntPtr selector);

        [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr objc_msgSend_long(IntPtr receiver, IntPtr selector, long value);

        [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr objc_msgSend_ulong(IntPtr receiver, IntPtr selector, ulong value);

        [DllImport(ObjCLib, EntryPoint = "sel_registerName")]
        public static extern IntPtr sel_registerName(string name);
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
            _bridgeContract = BridgeCommandContract.Load(
                Path.Combine(AppContext.BaseDirectory, "WebUi", "bridge-contract.json"));
            _bridgeContract.ValidateRegisteredCommands(_commandRouter.Commands);

            _server = LocalWebUiServer.Start(Path.Combine(AppContext.BaseDirectory, "WebUi"));
            _bridgeResponses.Attach(new AvaloniaBridgeTransport(_webView));
            _bridgeDispatcher = new BridgeDispatcher(
                DispatchAsync,
                _bridgeResponses,
                _bridgeContract,
                [_server.BaseUri.ToString()]);

            // 每次启动换一个查询串，避免 WKWebView 复用旧脚本缓存。
            var cacheKey = File.GetLastWriteTimeUtc(typeof(MainWindow).Assembly.Location).Ticks;
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.Source = new Uri(_server.BaseUri, "index.html?v=" + cacheKey.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Console.WriteLine($"[shell] 界面服务已就绪：{_server.BaseUri}");
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

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        var dispatcher = _bridgeDispatcher;
        if (dispatcher is null || _server is null) return;

        var body = e.Body;
        if (string.IsNullOrEmpty(body)) return;
        if (Environment.GetEnvironmentVariable("OPENCLAW_BRIDGE_LOG") == "1")
            Console.WriteLine($"[bridge←ui] {(body.Length <= 300 ? body : body[..300] + "…")}");

        // WKWebView 的消息事件不带来源地址，由壳声明自己的来源；
        // 越界导航由 OnNavigationCompleted 兜回，保证只有本地页面能持有这条通道。
        var envelope = new BridgeRequestEnvelope(_server.BaseUri.ToString(), body, []);
        _ = dispatcher.HandleAsync(envelope);
    }

    private async void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (_server is null) return;

        if (!e.IsSuccess)
        {
            Console.Error.WriteLine("[shell] 页面加载失败");
            return;
        }

        try
        {
            var href = await _webView.InvokeScript("location.href");
            if (href is null || !href.Contains(_server.BaseUri.Authority, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"[shell] 拦截越界导航：{href}");
                _webView.Source = _server.BaseUri;
                return;
            }

            if (_probed) return;
            _probed = true;

            var probe = await _webView.InvokeScript("""
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
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[shell] 页面就绪检查失败：{ex.Message}");
        }
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

    private void OnClosing(object? sender, WindowClosingEventArgs e)
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
}
