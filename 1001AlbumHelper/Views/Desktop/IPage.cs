using Avalonia.Controls;

namespace _1001AlbumHelper;

/// <summary>
/// One section of the main window's sidebar — Rate, Browse, Shortlist, Playlists. A page is built
/// the first time it's opened and then kept, so switching away and back keeps its place.
/// </summary>
public interface IPage
{
    /// <summary>
    /// Called every time the page comes on screen, the first time included: the first call loads
    /// it, later ones refresh whatever may have changed elsewhere in the app meanwhile.
    /// </summary>
    void OnShown();
}

/// <summary>A small window around one page, for the times a page is wanted as a dialog.</summary>
public sealed class PageWindow : Window
{
    public PageWindow(string title, Control page, double width = 700, double height = 680)
    {
        Title = title;
        Width = width;
        Height = height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Background = MainWindow.PageBackground();
        Content = page;
        Opened += (_, _) =>
        {
            if (page is IPage p) p.OnShown();
            page.Focus();
        };
    }
}
