using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

public partial class MainView : UserControl
{
    /// <summary>The nav buttons in tab order, so the pressed one can be lit and the rest dimmed.</summary>
    private readonly List<Button> _navButtons;

    public MainView()
    {
        InitializeComponent();

        _navButtons = [NavList, NavRate, NavShortlist, NavPlaylist1, NavPlaylist2];

        Playlist1View.Configure(1, "Playlist 1", "PLAYLIST1", "from the 1001");
        Playlist2View.Configure(2, "Playlist 2", "PLAYLIST2", "recommended");

        // Re-read a list whenever its tab comes forward, so changes made elsewhere are reflected
        // without restarting the app: albums added to a playlist from the List / Shortlist tabs, and
        // — the other direction — candidates a Playlist 2 pull has just put on the shortlist.
        Tabs.SelectionChanged += (_, _) =>
        {
            HighlightNav(Tabs.SelectedIndex);

            if (Tabs.SelectedIndex == 2) ShortlistView.Refresh();
            else if (Tabs.SelectedIndex == 3) Playlist1View.Refresh();
            else if (Tabs.SelectedIndex == 4) Playlist2View.Refresh();
        };
    }

    /// <summary>The bar replaces the TabControl's hidden strip, so a press has to move the selection.</summary>
    private void OnNavigate(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out int index))
            Tabs.SelectedIndex = index;
    }

    private void HighlightNav(int selected)
    {
        for (int i = 0; i < _navButtons.Count; i++)
            _navButtons[i].Classes.Set("on", i == selected);
    }
}
