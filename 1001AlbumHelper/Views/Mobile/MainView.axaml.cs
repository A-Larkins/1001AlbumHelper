using System.Collections.Generic;
using Avalonia.Controls;

namespace _1001AlbumHelper;

public partial class MainView : UserControl
{
    private List<Control> _screens = null!;

    public MainView()
    {
        InitializeComponent();

        Playlist1View.Configure(1, "Playlist 1 · from the 1001", "PLAYLIST1");
        Playlist2View.Configure(2, "Playlist 2 · recommended", "PLAYLIST2");

        _screens = new List<Control> { ListsScreen, RateScreen, PotentialsScreen, Playlist1View, Playlist2View };

        ScreenPicker.SelectionChanged += (_, _) => ShowScreen(ScreenPicker.SelectedIndex);
        // InitializeComponent already applied the XAML-declared SelectedIndex before the handler
        // above was attached, so the matching visibility/refresh needs applying once by hand here.
        ShowScreen(ScreenPicker.SelectedIndex);
    }

    private void ShowScreen(int index)
    {
        for (var i = 0; i < _screens.Count; i++)
            _screens[i].IsVisible = i == index;

        // Re-read the playlists whenever their screen comes forward, so albums added on
        // the Lists / Potentials screens are reflected without restarting the app.
        if (index == 3) Playlist1View.Refresh();
        else if (index == 4) Playlist2View.Refresh();
    }
}
