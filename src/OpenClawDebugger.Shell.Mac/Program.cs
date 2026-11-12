using AppKit;

namespace OpenClawDebugger;

/// <summary>
/// macOS 壳入口：纯 AppKit，不再依赖 Avalonia。
/// 窗口、标题栏、红绿灯、圆角、拖动全部走 Apple 原生 NSApplication/NSWindow。
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        NSApplication.Init();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Regular;

        var appDelegate = new AppDelegate();
        NSApplication.SharedApplication.Delegate = appDelegate;

        NSApplication.SharedApplication.Run();
    }
}
