namespace OpenClawDebugger;

/// <summary>
/// 首次运行时生成两个可切换的 SSH 目标：本机服务器与阿里云服务器。
/// 生成后写入 settings.json，之后以文件内容为准，用户可直接编辑该文件调整地址。
/// </summary>
public static class SshTargetDefaults
{
    public const string LocalId = "local";
    public const string RemoteId = "aliyun";

    /// <summary>新装环境下阿里云服务器的初始地址；已保存过远端连接时会被该连接覆盖。</summary>
    private const string SeedRemoteHost = "106.14.173.90";
    private const string SeedRemoteUser = "admin";
    private const string SeedRemoteHome = "/home/admin";

    /// <summary>目标列表为空时补齐默认的两项；已有内容时原样保留。返回是否发生了补齐。</summary>
    public static bool Ensure(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Targets ??= new List<SshTarget>();
        if (settings.Targets.Count > 0) return false;
        settings.Targets = Create(settings.Connection).ToList();
        return true;
    }

    public static IReadOnlyList<SshTarget> Create(ConnectionSettings? saved)
    {
        var connection = saved ?? new ConnectionSettings();
        var hasSaved = !string.IsNullOrWhiteSpace(connection.Host) && !string.IsNullOrWhiteSpace(connection.Username);
        var savedIsLocal = hasSaved && IsLoopback(connection.Host);

        var local = hasSaved && savedIsLocal
            ? FromConnection(LocalId, "本机服务器", connection)
            : BuildLocal();
        var remote = hasSaved && !savedIsLocal
            ? FromConnection(RemoteId, "阿里云服务器", connection)
            : BuildRemote();

        return new[] { local, remote };
    }

    private static SshTarget BuildLocal()
    {
        var home = PosixHome();
        return new SshTarget
        {
            Id = LocalId,
            Label = "本机服务器",
            Host = "127.0.0.1",
            Username = Environment.UserName,
            Port = 22,
            WorkspacePath = home + "/.openclaw/workspace",
            StickersPath = home + "/.openclaw/workspace/stickers"
        };
    }

    private static SshTarget BuildRemote() => new()
    {
        Id = RemoteId,
        Label = "阿里云服务器",
        Host = SeedRemoteHost,
        Username = SeedRemoteUser,
        Port = 22,
        WorkspacePath = SeedRemoteHome + "/.openclaw/workspace",
        StickersPath = SeedRemoteHome + "/.openclaw/workspace/stickers"
    };

    private static SshTarget FromConnection(string id, string label, ConnectionSettings connection) => new()
    {
        Id = id,
        Label = label,
        Host = connection.Host.Trim(),
        Username = connection.Username.Trim(),
        Port = connection.Port is >= 1 and <= 65535 ? connection.Port : 22,
        WorkspacePath = connection.WorkspacePath,
        StickersPath = connection.StickersPath
    };

    private static bool IsLoopback(string host) =>
        host is "127.0.0.1" or "localhost" or "::1" or "[::1]";

    /// <summary>本机目标必须用 POSIX 绝对路径，Windows 下把反斜杠规范成正斜杠。</summary>
    private static string PosixHome()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        home = (home ?? "").Replace('\\', '/').TrimEnd('/');
        return string.IsNullOrWhiteSpace(home) ? "/home/" + Environment.UserName : home;
    }
}
