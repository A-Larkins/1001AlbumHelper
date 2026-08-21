using System;
using Avalonia.Controls;

namespace _1001AlbumHelper;

/// <summary>
/// Dev-only: hosts the mobile <see cref="MainView"/> in a phone-sized desktop window, so mobile UI
/// changes can be eyeballed on the Mac without a device deploy round-trip. Run with
/// `dotnet run --project 1001AlbumHelper.Desktop -- mobilepreview`. No XAML — MainView is already
/// compiled here; this is just a plain window sized like an iPhone 15 Pro.
/// <para>
/// Set <c>ALBUMHELPER_PREVIEW_TAB</c> (0-4: List, Rate, Shortlist, Playlist 1, Playlist 2) to open
/// straight onto one screen. Without it the preview always starts on List, which means every other
/// screen needs a tap to reach — awkward when the point of the run is to look at one of them.
/// </para>
/// </summary>
public sealed class MobilePreviewWindow : Window
{
    public MobilePreviewWindow()
    {
        Title = "Mobile preview (iPhone 15 Pro size)";
        Width = 393;
        Height = 852;
        CanResize = false;

        var view = new MainView();
        Content = view;

        if (int.TryParse(Environment.GetEnvironmentVariable("ALBUMHELPER_PREVIEW_TAB"), out int tab))
            view.SelectTab(tab);
    }
}
