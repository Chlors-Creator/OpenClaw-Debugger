namespace OpenClawDebugger;

/// <summary>
/// SSH 客户端可执行文件的定位方式。Windows 需要看系统 OpenSSH / Git OpenSSH 的多个安装位置，
/// macOS 直接用系统自带的 /usr/bin/ssh。
/// </summary>
public interface ISshExecutableLocator
{
    /// <summary>返回可用的 ssh 可执行文件路径；找不到时应抛出说明性的 InvalidOperationException。</summary>
    string Resolve();
}

/// <summary>
/// 进程内的当前定位器。默认值是跨平台的 PATH 探测，Windows 壳会在启动时替换为
/// 带系统安装路径的实现。<see cref="SshCommandRunner"/> 通过它解析，避免改动全部调用点。
/// </summary>
public static class SshExecutableLocator
{
    private static ISshExecutableLocator _current = new PathSshExecutableLocator();

    public static ISshExecutableLocator Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }
}

/// <summary>
/// 与平台无关的兜底实现：先看环境变量 OPENCLAW_SSH_PATH，再扫描 PATH 中的 ssh / ssh.exe。
/// </summary>
public sealed class PathSshExecutableLocator : ISshExecutableLocator
{
    private static readonly string[] FileNames = ["ssh", "ssh.exe"];

    public string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("OPENCLAW_SSH_PATH")?.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var fileName in FileNames)
            {
                try
                {
                    var candidate = Path.Combine(directory, fileName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
            }
        }

        throw new InvalidOperationException(
            "找不到 SSH 客户端。请安装 OpenSSH，或设置 OPENCLAW_SSH_PATH 指向 ssh 可执行文件。" );
    }
}
