using AppKit;
using Foundation;

namespace OpenClawDebugger;

/// <summary>
/// NSApplicationDelegate：创建主窗口、处理应用激活与退出。
/// 业务逻辑全部在 <see cref="MainWindow"/> 里，这里只负责 AppKit 生命周期。
/// </summary>
internal sealed class AppDelegate : NSApplicationDelegate
{
    private MainWindow? _mainWindow;

    public override void DidFinishLaunching(NSNotification notification)
    {
        // Set the Dock icon explicitly at runtime. This also refreshes the
        // icon when Launch Services has cached the generic application icon.
        var icon = NSImage.ImageNamed("icon");
        var iconPath = NSBundle.MainBundle.PathForResource("icon", "icns")
            ?? Path.Combine(NSBundle.MainBundle.BundlePath, "Contents", "Resources", "icon.icns");
        if (icon is null || icon.Size.Width <= 0)
            icon = new NSImage(NSUrl.FromFilename(iconPath));
        if (icon is not null && icon.Size.Width > 0)
        {
            NSApplication.SharedApplication.ApplicationIconImage = icon;
            Console.WriteLine($"[shell] Dock 图标已加载：{iconPath} ({icon.Size.Width}x{icon.Size.Height})");
        }
        else
        {
            Console.Error.WriteLine($"[shell] Dock 图标加载失败：{iconPath}");
        }

        _mainWindow = new MainWindow();
        _mainWindow.MakeKeyAndOrderFront(null);

        // 首次启动激活应用并置前（从 Finder 双击启动时必要）。
        NSApplication.SharedApplication.Activate();
    }

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => true;

    public override void WillTerminate(NSNotification notification)
    {
        _mainWindow?.Dispose();
        _mainWindow = null;
    }
}
