using Avalonia;
using Avalonia.iOS;
using Foundation;

namespace _1001AlbumHelper;

// Bridges iOS's app lifecycle to the shared Avalonia App. Mirrors the desktop
// entry point (Program.cs): same App, same Inter font, but no UsePlatformDetect
// — the iOS backend is wired up by AvaloniaAppDelegate automatically.
[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .LogToTrace();
}
