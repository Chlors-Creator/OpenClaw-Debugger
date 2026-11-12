using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace OpenClawDebugger;

/// <summary>
/// 保证「记忆星海」本地面板（~/.openclaw/webui/memory-galaxy/server.mjs）在线，
/// 并返回可在应用内嵌 iframe 中打开的本地 URL。
///
/// 面板本身是只读的本地工具，只绑 127.0.0.1。这里复用了 <see cref="LocalGatewayLauncher"/>
/// 对 node 可执行目录的解析逻辑，因为从 Finder / 桌面启动时 PATH 里通常没有 node。
/// </summary>
public static class MemoryGalaxyLauncher
{
    public const int DefaultPort = 8791;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(8);

    /// <summary>记忆星海面板目录（server.mjs 所在目录）。</summary>
    public static string ResolveDirectory()
    {
        var stateDirectory = Environment.GetEnvironmentVariable("OPENCLAW_STATE_DIR");
        if (!string.IsNullOrWhiteSpace(stateDirectory))
            return Path.Combine(stateDirectory, "webui", "memory-galaxy");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".openclaw", "webui", "memory-galaxy");
    }

    /// <summary>判断目标端口是否已在监听。</summary>
    private static async Task<bool> IsListeningAsync(int port, CancellationToken cancellationToken)
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

    /// <summary>后台拉起 node server.mjs，日志落到系统临时目录，避免污染记忆目录。</summary>
    private static Process? StartServer(string serverScript, string nodeDirectory)
    {
        try
        {
            var executable = Path.Combine(nodeDirectory, OperatingSystem.IsWindows() ? "node.exe" : "node");
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(serverScript) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            startInfo.ArgumentList.Add(serverScript);
            startInfo.ArgumentList.Add("--port");
            startInfo.ArgumentList.Add(DefaultPort.ToString());

            // GUI 启动时 PATH 只含系统目录，补齐常用 bin，让 node 脚本能 resolve 依赖。
            LocalGatewayLauncher.ApplyChildPath(startInfo);

            var process = Process.Start(startInfo);
            if (process is null) return null;
            // 主动断开句柄，让面板进程独立于本应用存活；应用退出后面板仍可继续用。
            try { process.Close(); } catch { }
            return process;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// 确保面板在线并返回可打开的 URL。面板目录缺失或启动失败时返回 null，
    /// 由调用方决定如何向界面提示。
    /// </summary>
    public static async Task<string?> EnsureRunningAsync(CancellationToken cancellationToken)
    {
        if (await IsListeningAsync(DefaultPort, cancellationToken).ConfigureAwait(false))
            return $"http://127.0.0.1:{DefaultPort}/";

        var directory = ResolveDirectory();
        var serverScript = Path.Combine(directory, "server.mjs");
        if (!File.Exists(serverScript)) return null;

        var nodeDirectory = LocalGatewayLauncher.ResolveNodeDirectory();
        if (nodeDirectory is null) return null;

        StartServer(serverScript, nodeDirectory);

        var deadline = DateTimeOffset.UtcNow + ReadyTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await IsListeningAsync(DefaultPort, cancellationToken).ConfigureAwait(false))
                return $"http://127.0.0.1:{DefaultPort}/";
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
        return await IsListeningAsync(DefaultPort, cancellationToken).ConfigureAwait(false)
            ? $"http://127.0.0.1:{DefaultPort}/"
            : null;
    }
}
