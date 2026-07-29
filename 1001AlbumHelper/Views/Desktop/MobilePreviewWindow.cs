using Avalonia.Controls;

namespace _1001AlbumHelper;

/// <summary>
/// Dev-only: hosts the mobile <see cref="MainView"/> in a phone-sized desktop window, so mobile UI
/// changes can be eyeballed on the Mac without a device deploy round-trip. Run with
/// `dotnet run --project 1001AlbumHelper.Desktop -- mobilepreview`. No XAML — MainView is already
/// compiled here; this is just a plain window sized like an iPhone 15 Pro.
/// </summary>
public sealed class MobilePreviewWindow : Window
{
    public MobilePreviewWindow()
    {
        Title = "Mobile preview (iPhone 15 Pro size)";
        Width = 393;
        Height = 852;
        CanResize = false;
        Content = new MainView();
    }
}
