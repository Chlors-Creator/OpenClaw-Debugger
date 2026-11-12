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
        var iconPath = NSBundle.MainBundle.PathForResource("AppIcon", "icns");
        if (!string.IsNullOrEmpty(iconPath))
            NSApplication.SharedApplication.ApplicationIconImage = new NSImage(NSUrl.FromFilename(iconPath));

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
