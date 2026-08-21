using Avalonia.Controls;

namespace _1001AlbumHelper;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        Playlist1View.Configure(1, "Playlist 1 · from the 1001", "PLAYLIST1");
        Playlist2View.Configure(2, "Playlist 2 · recommended", "PLAYLIST2");

        // Re-read a list whenever its tab comes forward, so changes made elsewhere are reflected
        // without restarting the app: albums added to a playlist from the List / Shortlist tabs, and
        // — the other direction — candidates a Playlist 2 pull has just put on the shortlist.
        Tabs.SelectionChanged += (_, _) =>
        {
            if (Tabs.SelectedIndex == 2) ShortlistView.Refresh();
            else if (Tabs.SelectedIndex == 3) Playlist1View.Refresh();
            else if (Tabs.SelectedIndex == 4) Playlist2View.Refresh();
        };
    }
}
