using System.Diagnostics;
using System.IO;

namespace OpenClawDebugger;

/// <summary>
/// macOS 上的缩略图编码：调用系统自带的 sips(1) 完成缩放与 PNG 转码。
/// 保持零第三方依赖（不需要 SkiaSharp），代价是每次缓存未命中要起一个子进程，
/// 但缩略图本身有磁盘与内存两级缓存，实际开销可接受。
/// </summary>
public sealed class SipsThumbnailEncoder : IThumbnailEncoder
{
    private const string SipsPath = "/usr/bin/sips";

    public byte[] CreatePngThumbnail(byte[] original, int maxEdge)
    {
        if (original.Length == 0) throw new InvalidDataException("图片内容为空。");

        var workDirectory = Path.Combine(Path.GetTempPath(), "openclaw-thumb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        try
        {
            var inputPath = Path.Combine(workDirectory, "input");
            var outputPath = Path.Combine(workDirectory, "thumbnail.png");
            File.WriteAllBytes(inputPath, original);

            var startInfo = new ProcessStartInfo(SipsPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "-Z", maxEdge.ToString(), "-s", "format", "png", inputPath, "--out", outputPath })
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidDataException("无法启动 sips 生成缩略图。");
            var standardError = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0 || !File.Exists(outputPath))
                throw new InvalidDataException("sips 无法解码该图片：" + standardError.Trim());

            return File.ReadAllBytes(outputPath);
        }
        finally
        {
            try { Directory.Delete(workDirectory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

/// <summary>macOS 上在访达中打开目录。</summary>
public sealed class FinderShellLauncher : IShellLauncher
{
    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        using var process = Process.Start(new ProcessStartInfo("/usr/bin/open")
        {
            UseShellExecute = false,
            ArgumentList = { path }
        });
    }
}
