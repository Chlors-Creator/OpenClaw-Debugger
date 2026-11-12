using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace OpenClawDebugger;

/// <summary>
/// 连接本机目标时确保本机 OpenClaw 网关（Gateway）处于运行状态。
/// 远端目标不受影响：阿里云服务器上的网关不归本机进程管理。
/// </summary>
public static class LocalGatewayLauncher
{
    public const int DefaultPort = 18789;

    private const string GatewayLogFileName = "gateway.log";

    private static readonly System.Text.RegularExpressions.Regex AnsiEscapePattern =
        new("\u001B\\[[0-9;]*[A-Za-z]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly TimeSpan ServiceStartTimeout = TimeSpan.FromSeconds(20);
    /// <summary>启动后等待端口就绪的上限。连接本身不等这个，状态行先显示「启动中」。</summary>
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan ReclaimStaleAfter = TimeSpan.FromSeconds(60);

    /// <summary>后台启动的网关进程引用。应用退出后该子进程会继续运行，这里只是保留句柄。</summary>
    private static Process? _backgroundGateway;

    /// <summary>串行化启动流程，避免连续两次连接各起一个网关进程。</summary>
    private static readonly SemaphoreSlim StartGate = new(1, 1);

    /// <summary>判断 SSH 目标是否指向本机（127.0.0.0/8、localhost、::1）。</summary>
    public static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var value = host.Trim().TrimEnd('.').ToLowerInvariant();
        value = value.Trim('[', ']');
        if (value is "localhost" or "localhost.localdomain" or "::1" or "0:0:0:0:0:0:0:1") return true;
        var parts = value.Split('.');
        if (parts.Length != 4 || parts[0] != "127") return false;
        return parts.All(part => int.TryParse(part, out var number) && number is >= 0 and <= 255);
    }

    /// <summary>网关端口：环境变量优先，其次 OpenClaw 配置文件的 gateway.port。</summary>
    public static int ResolvePort()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("OPENCLAW_GATEWAY_PORT"), out var envPort) &&
            envPort is > 0 and <= 65535)
            return envPort;
        var configPath = ResolveConfigPath();
        if (configPath is not null && TryReadPort(configPath, out var configured)) return configured;
        return DefaultPort;
    }

    /// <summary>定位 OpenClaw 配置文件路径（遵循 CLI 的 OPENCLAW_CONFIG_PATH / OPENCLAW_STATE_DIR 约定）。</summary>
    public static string? ResolveConfigPath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("OPENCLAW_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath)) return explicitPath;

        var stateDirectory = ResolveStateDirectory();
        var candidate = Path.Combine(stateDirectory, "openclaw.json");
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>OpenClaw 状态目录，默认 ~/.openclaw。</summary>
    public static string ResolveStateDirectory()
    {
        var stateDirectory = Environment.GetEnvironmentVariable("OPENCLAW_STATE_DIR");
        if (!string.IsNullOrWhiteSpace(stateDirectory)) return stateDirectory;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".openclaw");
    }

    /// <summary>从配置文件中读取网关端口；只关心 gateway.port，其余字段一律忽略。</summary>
    public static bool TryReadPort(string configPath, out int port)
    {
        port = 0;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty("gateway", out var gateway) ||
                gateway.ValueKind != JsonValueKind.Object) return false;
            if (!gateway.TryGetProperty("port", out var element)) return false;

            var value = element.ValueKind switch
            {
                JsonValueKind.Number when element.TryGetInt32(out var number) => number,
                JsonValueKind.String when int.TryParse(element.GetString(), out var parsed) => parsed,
                _ => 0
            };
            if (value is <= 0 or > 65535) return false;
            port = value;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>定位 openclaw 可执行文件：OPENCLAW_CLI 优先，其次 PATH，最后常见安装位置。</summary>
    public static string? ResolveExecutable()
    {
        var explicitPath = Environment.GetEnvironmentVariable("OPENCLAW_CLI");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath)) return explicitPath;

        var names = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "openclaw.cmd", "openclaw.exe", "openclaw" }
            : ["openclaw"];

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ?
            [
                Path.Combine(home, "AppData", "Roaming", "npm", "openclaw.cmd"),
                Path.Combine(home, "AppData", "Local", "Programs", "openclaw", "openclaw.exe")
            ]
            :
            [
                "/usr/local/bin/openclaw",
                "/opt/homebrew/bin/openclaw",
                "/usr/bin/openclaw",
                Path.Combine(home, ".local", "bin", "openclaw"),
                Path.Combine(home, ".npm-global", "bin", "openclaw"),
                Path.Combine(home, ".openclaw", "bin", "openclaw")
            ];
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// 定位 node 所在目录。openclaw 是 <c>#!/usr/bin/env node</c> 脚本，
    /// 从 Finder/launchd 启动应用时 PATH 里通常没有 node，会直接报 "env: node: No such file or directory"。
    /// </summary>
    public static string? ResolveNodeDirectory()
    {
        var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "node.exe" : "node";
        var searchedPaths = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in searchedPaths)
        {
            if (File.Exists(Path.Combine(directory, executableName))) return directory;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            candidates.Add(Path.Combine(home, "AppData", "Roaming", "npm"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"));
        }
        else
        {
            candidates.Add("/usr/local/bin");
            candidates.Add("/opt/homebrew/bin");
            candidates.Add(Path.Combine(home, ".local", "bin"));
            candidates.Add(Path.Combine(home, ".volta", "bin"));
            // 版本管理器的布局是 <root>/<version>/bin，逐个展开。
            foreach (var root in new[]
                     {
                         Path.Combine(home, ".nvm", "versions", "node"),
                         Path.Combine(home, ".workbuddy", "binaries", "node", "versions")
                     })
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var version in Directory.EnumerateDirectories(root))
                        candidates.Add(Path.Combine(version, "bin"));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 列目录失败就跳过，不影响后面的候选。
                }
            }
        }
        return candidates.FirstOrDefault(directory => File.Exists(Path.Combine(directory, executableName)));
    }

    /// <summary>给子进程补上 node 与常见 bin 目录：GUI 启动的 PATH 只含系统目录。</summary>
    public static void ApplyChildPath(ProcessStartInfo startInfo)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var parts = new List<string>();
        var nodeDirectory = ResolveNodeDirectory();
        if (!string.IsNullOrEmpty(nodeDirectory)) parts.Add(nodeDirectory);
        parts.AddRange(["/usr/local/bin", "/opt/homebrew/bin", "/usr/bin", "/bin", "/usr/sbin", "/sbin"]);
        var existing = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(existing)) parts.Add(existing);
        startInfo.Environment["PATH"] = string.Join(
            Path.PathSeparator, parts.Distinct(StringComparer.Ordinal));
    }

    /// <summary>
    /// 保证本机网关在线。仅对本机目标生效；启动失败不抛异常，
    /// 由调用方决定如何展示（文件扫描本身并不依赖网关）。
    /// </summary>
    public static async Task<LocalGatewayStartResult> EnsureRunningAsync(
        string? host,
        string? logDirectory,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!IsLoopbackHost(host)) return LocalGatewayStartResult.NotApplicable();

        await StartGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await EnsureRunningCoreAsync(host, logDirectory, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            StartGate.Release();
        }
    }

    private static async Task<LocalGatewayStartResult> EnsureRunningCoreAsync(
        string? host,
        string? logDirectory,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        var port = ResolvePort();
        if (await IsListeningAsync(port, cancellationToken).ConfigureAwait(false))
            return new LocalGatewayStartResult(true, true, false, false, $"本机网关已在线（127.0.0.1:{port}）", port, null);

        var cleaned = CleanupStaleReclaimMarkers(ResolveStateDirectory(), ReclaimStaleAfter);
        if (cleaned.Count > 0)
            Report("gateway-stale-state-cleared", string.Join("；", cleaned));

        var executable = ResolveExecutable();
        if (executable is null)
        {
            const string missing = "未找到 openclaw 命令，无法自动启动本机网关。";
            Report("gateway-executable-missing", missing);
            return new LocalGatewayStartResult(true, false, false, false, missing, port, "PATH 中没有 openclaw");
        }

        progress?.Invoke("正在启动本机 OpenClaw 网关…");

        // 已安装 launchd/systemd 服务时优先交给服务管理器，否则直接后台拉起前台网关（冷启动约 30–90 秒）。
        if (IsServiceInstalled())
        {
            var service = await RunAsync(executable, ["gateway", "start"], ServiceStartTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (await WaitUntilListeningAsync(port, ReadyTimeout, cancellationToken).ConfigureAwait(false))
                return Ready(startedNow: true, port, "已启动本机 OpenClaw 网关服务");
            Report("gateway-service-start-failed", Summarize(service.Output));
        }

        progress?.Invoke("正在后台启动本机 OpenClaw 网关…");
        var logPath = EnsureGatewayLogFile(logDirectory);
        var process = StartBackgroundGateway(executable, logPath);
        if (process is null)
        {
            Report("gateway-spawn-failed", "无法创建 openclaw 网关进程。");
            return new LocalGatewayStartResult(true, false, false, false, "无法启动本机 OpenClaw 网关进程。", port, logPath);
        }

        if (!await WaitUntilListeningAsync(port, ReadyTimeout, cancellationToken).ConfigureAwait(false))
        {
            var detail = Summarize(TailGatewayLog(logPath));
            Report("gateway-start-pending", detail);
            var alive = IsRunning(process);
            var message = !alive
                ? $"本机网关启动失败（127.0.0.1:{port}）。"
                : detail.Contains("state ownership", StringComparison.OrdinalIgnoreCase)
                    // 上一个网关进程没有正常退出：OpenClaw 会等租约过期后接管，最长 5 分钟。
                    ? $"本机网关正在接管上一个进程（127.0.0.1:{port}，最多约 5 分钟）。"
                    : $"本机网关正在启动（127.0.0.1:{port}，首次冷启动约 1 分钟）。";
            return new LocalGatewayStartResult(true, false, false, alive, message, port,
                detail.Length == 0 ? logPath : detail);
        }

        return Ready(startedNow: true, port, "已在后台启动本机 OpenClaw 网关");
    }

    private static bool IsRunning(Process process)
    {
        try { return !process.HasExited; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>网关是否已经注册为系统服务（launchd / systemd）。未安装时不必浪费一次 start 调用。</summary>
    public static bool IsServiceInstalled()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return File.Exists(Path.Combine(home, "Library", "LaunchAgents", "ai.openclaw.gateway.plist"));
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return File.Exists(Path.Combine(home, ".config", "systemd", "user", "openclaw-gateway.service")) ||
                   File.Exists("/etc/systemd/system/openclaw-gateway.service");
        return false;
    }

    private static LocalGatewayStartResult Ready(bool startedNow, int port, string prefix)
    {
        var message = $"{prefix}（127.0.0.1:{port}）。";
        Report("gateway-started", message);
        return new LocalGatewayStartResult(true, true, startedNow, false, message, port, null);
    }

    /// <summary>本机端口是否已在监听。</summary>
    public static async Task<bool> IsListeningAsync(int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// 清理上次异常退出留下的 reclaim 占位目录：它们存在时新网关会以为有人正在接管，
    /// 白等最多 300 秒。这里只删空目录，且只在超过 <paramref name="minAge"/> 之后。
    /// </summary>
    public static IReadOnlyList<string> CleanupStaleReclaimMarkers(string stateDirectory, TimeSpan minAge)
    {
        var removed = new List<string>();
        var tempRoot = Path.Combine(stateDirectory, "tmp");
        if (!Directory.Exists(tempRoot)) return removed;

        foreach (var directory in Directory.EnumerateDirectories(tempRoot, "openclaw-*"))
        {
            foreach (var reclaim in Directory.EnumerateDirectories(directory, "*.lock.reclaim"))
            {
                try
                {
                    var info = new DirectoryInfo(reclaim);
                    if (info.LastWriteTimeUtc > DateTime.UtcNow - minAge) continue;
                    if (info.EnumerateFileSystemInfos().Any()) continue;
                    info.Delete();
                    removed.Add(info.Name);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 目录被占用时跳过，下一次连接再处理。
                }
            }
        }
        return removed;
    }

    /// <summary>读取锁文件中的 pid；格式不认识就当作无法判断。</summary>
    public static bool TryReadLockPid(string lockPath, out int pid)
    {
        pid = 0;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(lockPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty("pid", out var element)) return false;
            if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value)) return false;
            if (value <= 0) return false;
            pid = value;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static string? EnsureGatewayLogFile(string? logDirectory)
    {
        if (string.IsNullOrWhiteSpace(logDirectory)) return null;
        try
        {
            Directory.CreateDirectory(logDirectory);
            var path = Path.Combine(logDirectory, GatewayLogFileName);
            var info = new FileInfo(path);
            if (info.Exists && info.Length > 4L * 1024 * 1024)
            {
                var backup = path + ".1";
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(path, backup);
            }
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 后台拉起 <c>openclaw gateway run</c>。Unix 上通过 sh 重定向日志并 exec，
    /// 进程句柄保留在静态字段里，应用退出后网关继续运行。
    /// </summary>
    private static Process? StartBackgroundGateway(string executable, string? logPath)
    {
        try
        {
            ProcessStartInfo startInfo;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("gateway");
                startInfo.ArgumentList.Add("run");
            }
            else
            {
                startInfo = new ProcessStartInfo("/bin/sh")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("-c");
                // $0 = 可执行文件，$1 = 日志路径：交给 sh 处理引号，避免手工拼接转义。
                startInfo.ArgumentList.Add(string.IsNullOrEmpty(logPath)
                    ? "exec \"$0\" gateway run >/dev/null 2>&1"
                    : "exec \"$0\" gateway run >>\"$1\" 2>&1");
                startInfo.ArgumentList.Add(executable);
                if (!string.IsNullOrEmpty(logPath)) startInfo.ArgumentList.Add(logPath);
                ApplyChildPath(startInfo);
            }

            var process = Process.Start(startInfo);
            if (process is null) return null;
            _backgroundGateway = process;
            return process;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Report("gateway-spawn-failed", ex.Message);
            return null;
        }
    }

    private static async Task<bool> WaitUntilListeningAsync(int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await IsListeningAsync(port, cancellationToken).ConfigureAwait(false)) return true;
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
        return await IsListeningAsync(port, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        ApplyChildPath(startInfo);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return new CommandResult(false, -1, "无法启动 openclaw 命令。");

            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return new CommandResult(false, -1, "openclaw 命令超时。");
            }

            var output = ((await stdout.ConfigureAwait(false)) + "\n" + (await stderr.ConfigureAwait(false))).Trim();
            return new CommandResult(process.ExitCode == 0, process.ExitCode, output);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new CommandResult(false, -1, ex.Message);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // 进程可能已经退出，忽略。
        }
    }

    private static string TailGatewayLog(string? logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath)) return "";
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = stream.Length;
            var start = Math.Max(0, length - 8 * 1024);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static string Summarize(string output)
    {
        var lines = AnsiEscapePattern.Replace(output, "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("(node:", StringComparison.Ordinal) &&
                           !line.StartsWith("(Use `node", StringComparison.Ordinal) &&
                           !line.Contains("[gateway] startup phase:", StringComparison.Ordinal))
            .ToArray();
        var text = string.Join(" ", lines.TakeLast(6));
        return text.Length > 400 ? text[^400..] : text;
    }

    private static void Report(string stage, string message)
    {
        try { OperationLogStore.Current?.Stage(stage, message); }
        catch { }
    }

    private sealed record CommandResult(bool Success, int ExitCode, string Output);
}

/// <summary>本机网关自动启动的结果。<see cref="Applicable"/> 为 false 表示目标不是本机。</summary>
public sealed record LocalGatewayStartResult(
    bool Applicable,
    bool Running,
    bool StartedNow,
    bool Pending,
    string Message,
    int Port,
    string? Detail)
{
    public static LocalGatewayStartResult NotApplicable() => new(false, false, false, false, "", 0, null);

    /// <summary>拼接在连接状态后面的一小段文案。</summary>
    public string ShortStatus => !Applicable
        ? ""
        : Running
            ? (StartedNow ? "本机网关已启动" : "本机网关在线")
            : Pending ? "本机网关启动中" : "本机网关未启动";
}
