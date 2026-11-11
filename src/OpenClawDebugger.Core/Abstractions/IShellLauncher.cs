namespace OpenClawDebugger;

/// <summary>在系统文件管理器中打开目录。Windows 用 explorer，macOS 用 open。</summary>
public interface IShellLauncher
{
    void OpenFolder(string path);
}
