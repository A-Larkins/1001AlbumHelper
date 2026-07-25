using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>
/// Shows one on-device playlist (1 = from the 1001, 2 = potential replacements) and lets the user
/// remove entries or push the whole thing to Apple Music. Reloaded each time its tab is shown so it
/// reflects albums added over on the List/Replacements tabs.
/// </summary>
public partial class PlaylistView : UserControl
{
    private int _playlistId;
    private PlaylistStore? _store;

    public PlaylistView()
    {
        InitializeComponent();
    }

    /// <summary>Points this view at playlist <paramref name="id"/> and gives it a heading.</summary>
    public void Configure(int id, string title)
    {
        _playlistId = id;
        TitleText.Text = title;
        Refresh();
    }

    /// <summary>Re-reads the playlist from disk so newly added albums show up.</summary>
    public void Refresh()
    {
        if (_playlistId == 0) return;
        _store = PlaylistStore.Open(_playlistId);
        Rows.ItemsSource = _store.Entries;
        CountText.Text = _store.Entries.Count == 1 ? "1 album" : $"{_store.Entries.Count} albums";
        AppleMusicButton.IsEnabled = _store.Entries.Count > 0;
    }

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (_store is null || sender is not Button button || button.DataContext is not PlaylistEntry entry) return;
        _store.Remove(entry);
        Refresh();
    }

    private void OnAddAllToAppleMusic(object? sender, RoutedEventArgs e)
    {
        // Apple Music wiring lands in a later step; for now report what would be pushed.
        int count = _store?.Entries.Count ?? 0;
        StatusText.IsVisible = true;
        StatusText.Text = $"Apple Music sync coming soon — {count} album(s) queued locally.";
    }
}
