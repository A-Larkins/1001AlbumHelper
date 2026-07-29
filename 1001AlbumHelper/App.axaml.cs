using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace _1001AlbumHelper;

public partial class App : Application
{
    /// <summary>Dev-only: set from Program.cs's `mobilepreview` arg to show the mobile view instead of MainWindow.</summary>
    public static bool PreviewMobile;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Desktop (Mac/Windows/Linux): a top-level window.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = PreviewMobile ? new MobilePreviewWindow() : new MainWindow();
        }
        // Mobile (iOS/Android): no OS windows — a single root view fills the screen.
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
