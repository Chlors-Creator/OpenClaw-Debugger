namespace OpenClawDebugger;

/// <summary>连接生命周期、远程客户端装配和一次连接扫描的协调器。</summary>
public sealed class ConnectionCoordinator : IDisposable
{
    private readonly Func<bool> _isBusy;
    private readonly Action<bool> _setBusy;
    private readonly Action<string, object> _sendProgress;
    private readonly ICredentialStore _credentialStore;
    private readonly ISecretProtector _secretProtector;
    private readonly IThumbnailEncoder _thumbnailEncoder;
    private readonly RemoteOpenClawClient _remote = new();
    private readonly RemoteFileClient _remoteFiles;
    private readonly RemoteStickerClient _remoteStickers;
    private readonly RemoteSnapshotClient _snapshotClient = new();
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private LocalSnapshotStore? _snapshots;
    private UserSettings? _settings;
    private bool _initialized;
    private bool _disposed;

    public ConnectionCoordinator(
        Func<bool> isBusy,
        Action<bool> setBusy,
        Action<string, object> sendProgress,
        ICredentialStore credentialStore,
        ISecretProtector secretProtector,
        IThumbnailEncoder thumbnailEncoder)
    {
        _isBusy = isBusy;
        _setBusy = setBusy;
        _sendProgress = sendProgress;
        _credentialStore = credentialStore;
        _secretProtector = secretProtector;
        _thumbnailEncoder = thumbnailEncoder;
        _remoteFiles = new RemoteFileClient(_remote);
        _remoteStickers = new RemoteStickerClient(_remote);
    }

    public MemoryService? Memory { get; private set; }
    public StickerService? Stickers { get; private set; }
    public ModelService? Models { get; private set; }
    public PluginService? Plugins { get; private set; }
    public SkillService? Skills { get; private set; }
    public BackupCoordinator? Backup { get; private set; }
    public IReadOnlyList<RemoteFile> Files { get; private set; } = [];
    public bool IsConnected { get; private set; }

    public void Initialize(UserSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        if (_initialized) throw new InvalidOperationException("连接协调器已经初始化。" );

        _settings = settings;
        _snapshots = new LocalSnapshotStore(settings.PrivateDirectory, _secretProtector);
        Memory = new MemoryService(
            _remoteFiles,
            () => settings.Connection,
            () => _snapshots,
            () => Files,
            () => IsConnected,
            _setBusy);
        Stickers = new StickerService(
            _remoteFiles,
            _remoteStickers,
            new StickerThumbnailCache(settings.PrivateDirectory, _thumbnailEncoder),
            new StickerUploadService(settings.PrivateDirectory),
            () => settings.Connection,
            () => _snapshots,
            () => settings.PrivateDirectory,
            () => IsConnected,
            _isBusy,
            value => IsConnected = value,
            _setBusy,
            (bytes, total) => _sendProgress("upload", new { phase = "transferring", bytes, totalBytes = total }));
        Models = new ModelService(
            new RemoteModelClient(_remote, _credentialStore),
            () => settings.Connection,
            () => IsConnected,
            settings.PrivateDirectory,
            _setBusy);
        Plugins = new PluginService(
            new RemotePluginClient(_remote),
            () => settings.Connection,
            () => IsConnected,
            _setBusy);
        Skills = new SkillService(new SkillServiceDependencies(
            new RemoteSkillClient(_remote),
            () => settings.Connection,
            () => IsConnected,
            _setBusy));
        Backup = new BackupCoordinator(
            _snapshotClient,
            SettingsRepository.DefaultBackupDirectory,
            _isBusy,
            _setBusy,
            _sendProgress,
            () => settings.BackupRetentionCount);
        _initialized = true;
    }

    public async Task<object> ConnectAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized || !ReferenceEquals(_settings, settings))
            throw new InvalidOperationException("连接协调器尚未使用当前设置初始化。" );
        await _connectGate.WaitAsync(cancellationToken);
        try
        {
            if (_isBusy()) throw new InvalidOperationException("当前有操作正在进行。" );
            _setBusy(true);
            try
            {
                Models?.ResetCache();
                await SettingsRepository.SaveAsync(settings);

                // 本机目标：连接时顺带把本机 OpenClaw 网关拉起来。启动在后台进行，
                // 不阻塞文件扫描；就绪后再把状态行从「启动中」改成「已启动」。
                var gatewayPort = LocalGatewayLauncher.ResolvePort();
                var gatewayTask = StartLocalGatewayAsync(settings.Connection);

                Files = await _remoteFiles.ConnectAndListAsync(settings.Connection, cancellationToken);
                IsConnected = true;
                Memory?.Reset();
                var stickerService = Stickers ?? throw new InvalidOperationException("表情包服务未初始化。" );
                var stickerLoad = await stickerService.LoadAsync(Files, cancellationToken);
                var memories = Files.Where(x => x.Root == "workspace" && !x.IsImage && x.Editable)
                    .OrderBy(x => x.RelativePath.StartsWith("memory/", StringComparison.Ordinal) ? 1 : 0)
                    .ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var imagesCount = Files.Count(x => x.Root == "stickers" && x.IsImage);
                var status = "已连接 · " + memories.Count + " 个文档 · " + imagesCount + " 张图片";
                if (gatewayTask is not null)
                {
                    var online = await LocalGatewayLauncher.IsListeningAsync(gatewayPort, cancellationToken);
                    status += online ? " · 本机网关在线" : " · 本机网关启动中";
                    WatchGateway(gatewayTask, status);
                }
                return new ConnectionScanResult(
                    true,
                    status,
                    memories,
                    memories.Count,
                    stickerLoad.StickerFiles,
                    stickerLoad.StickerCount,
                    stickerLoad.StickerEditingEnabled,
                    stickerLoad.CatalogText,
                    stickerLoad.ManifestText,
                    stickerLoad.StickerStatus,
                    settings.Connection.WorkspacePath,
                    settings.Connection.StickersPath);
            }
            catch
            {
                IsConnected = false;
                throw;
            }
            finally
            {
                _setBusy(false);
            }
        }
        finally { _connectGate.Release(); }
    }

    /// <summary>本机目标在后台确保网关在线；远端目标返回 null，表示这一趟不需要管网关。</summary>
    private Task<LocalGatewayStartResult>? StartLocalGatewayAsync(ConnectionSettings connection)
    {
        if (!LocalGatewayLauncher.IsLoopbackHost(connection.Host)) return null;

        var logDirectory = string.IsNullOrWhiteSpace(_settings?.PrivateDirectory)
            ? null
            : Path.Combine(_settings.PrivateDirectory, "Logs");
        _sendProgress("gateway", new { phase = "checking", message = "正在检查本机 OpenClaw 网关…" });

        // 不跟随连接请求的取消：用户点了连接就该把本机服务器开起来。
        var task = Task.Run(() => LocalGatewayLauncher.EnsureRunningAsync(
            connection.Host,
            logDirectory,
            message => _sendProgress("gateway", new { phase = "starting", message }),
            CancellationToken.None));

        // 连接本身失败时 WatchGateway 不会挂上去，这里兜住异常避免变成未观察任务。
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    /// <summary>后台启动完成后更新状态行：就绪改成「已启动」，失败给出提示。</summary>
    private void WatchGateway(Task<LocalGatewayStartResult> task, string connectedStatus)
    {
        _ = task.ContinueWith(completed =>
        {
            if (!completed.IsCompletedSuccessfully) return;
            var gateway = completed.Result;
            if (!gateway.Applicable) return;
            if (gateway.Running)
            {
                if (gateway.StartedNow)
                    _sendProgress("gateway", new
                    {
                        phase = "online",
                        message = connectedStatus.Replace("本机网关启动中", "本机网关已启动", StringComparison.Ordinal)
                    });
                return;
            }
            _sendProgress("gateway", new
            {
                phase = gateway.Pending ? "pending" : "failed",
                message = gateway.Message,
                detail = gateway.Detail
            });
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public async Task<object> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _connectGate.WaitAsync(cancellationToken);
        try
        {
            if (_isBusy()) throw new InvalidOperationException("当前有操作正在进行，请等待完成或先取消当前操作。" );
            _setBusy(true);
            try
            {
                IsConnected = false;
                Files = [];
                Memory?.Reset();
                Models?.ResetCache();
                _remote.Disconnect();
                return new { Connected = false };
            }
            finally
            {
                _setBusy(false);
            }
        }
        finally { _connectGate.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Backup?.Dispose();
        Models?.Dispose();
        Stickers?.Dispose();
        _snapshotClient.Dispose();
        _remote.Dispose();
    }
}

public sealed record ConnectionScanResult(
    bool Connected,
    string ConnectionStatus,
    IReadOnlyList<RemoteFile> MemoryFiles,
    int MemoryCount,
    IReadOnlyList<StickerUiRow> StickerFiles,
    int StickerCount,
    bool StickerEditingEnabled,
    string? CatalogText,
    string? ManifestText,
    string StickerStatus,
    string WorkspacePath,
    string StickersPath);
