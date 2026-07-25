using Avalonia.Controls;

namespace _1001AlbumHelper;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        Playlist1View.Configure(1, "Playlist 1 · from the 1001", "1001 Albums – My Picks");
        Playlist2View.Configure(2, "Playlist 2 · recommended", "1001 Albums – Recommended");

        // Re-read the playlists whenever their tab comes forward, so albums added on the
        // List / Replacements tabs are reflected without restarting the app.
        Tabs.SelectionChanged += (_, _) =>
        {
            if (Tabs.SelectedIndex == 2) Playlist1View.Refresh();
            else if (Tabs.SelectedIndex == 3) Playlist2View.Refresh();
        };
    }
}
