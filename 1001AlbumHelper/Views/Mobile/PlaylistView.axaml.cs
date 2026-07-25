using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>
/// Shows one on-device playlist (1 = from the 1001, 2 = potential replacements) and lets the user
/// remove entries or push the whole thing to an Apple Music library playlist. Reloaded each time its
/// tab is shown so it reflects albums added over on the List / Replacements tabs.
/// </summary>
public partial class PlaylistView : UserControl
{
    private int _playlistId;
    private string _appleMusicName = "";
    private PlaylistStore? _store;

    public PlaylistView() => InitializeComponent();

    /// <summary>Points this view at playlist <paramref name="id"/>, with a heading and the Apple Music playlist name.</summary>
    public void Configure(int id, string title, string appleMusicName)
    {
        _playlistId = id;
        _appleMusicName = appleMusicName;
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

    private async void OnAddAllToAppleMusic(object? sender, RoutedEventArgs e)
    {
        if (_store is null) return;
        var albums = _store.Entries.ToList();
        if (albums.Count == 0) return;

        StatusText.IsVisible = true;

        // On desktop there's no Apple Music writer; only the iPhone app registers one.
        if (!AppleMusic.IsAvailable)
        {
            StatusText.Text = "Adding to Apple Music works in the iPhone app.";
            return;
        }

        AppleMusicButton.IsEnabled = false;
        var progress = new Progress<string>(message => StatusText.Text = message);
        try
        {
            var result = await AppleMusic.Writer!.AddAlbumsAsync(_appleMusicName, albums, progress);
            StatusText.Text = result.Summary;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't sync to Apple Music: {ex.Message}";
        }
        finally
        {
            AppleMusicButton.IsEnabled = true;
        }
    }
}
