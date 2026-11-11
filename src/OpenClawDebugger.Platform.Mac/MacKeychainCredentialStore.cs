using System.Diagnostics;
using System.IO;
using System.Text;

namespace OpenClawDebugger;

/// <summary>
/// macOS Keychain 适配层，通过系统自带的 security(1) 命令行读写通用密码项。
/// 与 Windows 凭据管理器保持一致的"只存不透明目标名 + 密钥本体"模型。
/// </summary>
/// <remarks>
/// 注意：secret 通过 -w 参数传入，会短暂出现在进程参数表里。这与多数 macOS 命令行工具的
/// 做法一致，但正式发布前建议改为 Security.framework 的 SecItemAdd 互操作调用。
/// </remarks>
public sealed class MacKeychainCredentialStore : ICredentialStore
{
    private const string Account = "OpenClawDebugger";
    private const string SecurityPath = "/usr/bin/security";

    public string? Read(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var result = Run("find-generic-password", "-a", Account, "-s", target, "-w");
        // 找不到条目时 security 返回 44，属于正常的未命中。
        if (result.ExitCode != 0) return null;
        return result.StandardOutput.TrimEnd('\n');
    }

    public void Write(string target, string secret)
    {
        if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("凭据目标不能为空。", nameof(target));
        ArgumentNullException.ThrowIfNull(secret);
        // -U 表示已存在则更新，避免重复条目。
        var result = Run("add-generic-password", "-a", Account, "-s", target, "-w", secret, "-U");
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Keychain 写入失败：" + result.StandardError.Trim());
    }

    public void Delete(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        var result = Run("delete-generic-password", "-a", Account, "-s", target);
        if (result.ExitCode != 0 && !result.StandardError.Contains("could not be found", StringComparison.Ordinal))
            throw new InvalidOperationException("Keychain 删除失败：" + result.StandardError.Trim());
    }

    private static (int ExitCode, string StandardOutput, string StandardError) Run(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(SecurityPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 security 命令行工具。");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, standardOutput, standardError);
    }
}

/// <summary>macOS 上的 ssh 定位：优先 Homebrew 与系统自带路径，最后退回 PATH 扫描。</summary>
public sealed class UnixSshLocator : ISshExecutableLocator
{
    private static readonly string[] Candidates =
    [
        "/usr/bin/ssh",
        "/opt/homebrew/bin/ssh",
        "/usr/local/bin/ssh"
    ];

    public string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("OPENCLAW_SSH_PATH")?.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        foreach (var candidate in Candidates)
            if (File.Exists(candidate)) return candidate;

        return new PathSshExecutableLocator().Resolve();
    }
}
