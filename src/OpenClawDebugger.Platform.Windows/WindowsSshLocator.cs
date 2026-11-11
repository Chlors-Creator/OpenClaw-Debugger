using System.Diagnostics;
using System.IO;

namespace OpenClawDebugger;

/// <summary>
/// Windows 上的 ssh 定位：先看 OPENCLAW_SSH_PATH，再依次检查系统 OpenSSH 与 Git 自带的
/// OpenSSH 安装位置，最后才退回 PATH 扫描。逻辑从 SshCommandRunner 原样搬出。
/// </summary>
public sealed class WindowsSshLocator : ISshExecutableLocator
{
    private static readonly string[] CommonExecutablePaths = BuildCandidatePaths();

    public string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("OPENCLAW_SSH_PATH")?.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        foreach (var candidate in CommonExecutablePaths.Where(x => !string.IsNullOrWhiteSpace(x)))
            if (File.Exists(candidate)) return candidate;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, "ssh.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }

        throw new InvalidOperationException(
            "找不到 Windows OpenSSH 客户端 ssh.exe。已检查系统 OpenSSH、Git OpenSSH 和当前 PATH；" +
            "请安装 Windows OpenSSH Client，或设置 OPENCLAW_SSH_PATH 指向 ssh.exe。\n已检查：" +
            string.Join("；", CommonExecutablePaths.Where(x => !string.IsNullOrWhiteSpace(x))));
    }

    private static string[] BuildCandidatePaths()
    {
        try
        {
            return
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "OpenSSH", "ssh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative", "OpenSSH", "ssh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "OpenSSH", "ssh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenSSH", "ssh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "usr", "bin", "ssh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "OpenSSH", "ssh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "usr", "bin", "ssh.exe")
            ];
        }
        catch (PlatformNotSupportedException)
        {
            // 理论上只会在非 Windows 上被误用，此时直接跳过候选路径，退回 PATH 扫描。
            return [];
        }
    }
}

/// <summary>Windows 上在资源管理器中打开目录。</summary>
public sealed class ExplorerShellLauncher : IShellLauncher
{
    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true,
            ArgumentList = { path }
        });
    }
}
