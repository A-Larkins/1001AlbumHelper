using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>
/// One playlist "working list" (1 = from the 1001, 2 = potential replacements). This is the clean,
/// fully add/remove list the user manages; it maps to an Apple Music playlist by name for listening.
/// "Import" seeds it from what's already in that Apple Music playlist; "Push all" adds the working
/// list into Apple Music (add-only — Apple has no remove, so cleanup happens here in the app).
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

    /// <summary>Re-reads the working list from disk so newly added albums show up.</summary>
    public void Refresh()
    {
        if (_playlistId == 0) return;
        _store = PlaylistStore.Open(_playlistId);
        Rows.ItemsSource = _store.Entries;
        CountText.Text = _store.Entries.Count == 1 ? "1 album" : $"{_store.Entries.Count} albums";
        ImportButton.IsEnabled = AppleMusic.IsAvailable;
        PushButton.IsEnabled = AppleMusic.IsAvailable && _store.Entries.Count > 0;
    }

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (_store is null || sender is not Button button || button.DataContext is not PlaylistEntry entry) return;
        _store.Remove(entry);
        Refresh();
    }

    /// <summary>Reads the existing Apple Music playlist and merges anything new into the working list.</summary>
    private async void OnImportFromAppleMusic(object? sender, RoutedEventArgs e)
    {
        if (_store is null) return;
        if (!AppleMusic.IsAvailable) { Note("Apple Music is only available in the iPhone app."); return; }

        SetBusy(true, $"Reading “{_appleMusicName}” from Apple Music…");
        try
        {
            var albums = await AppleMusic.Writer!.ReadAlbumsAsync(_appleMusicName);
            int added = albums.Count(a => _store.Add(a.Title, a.Artist, a.Year));
            Refresh();
            Note($"Imported {added} new ({albums.Count} in “{_appleMusicName}”).");
        }
        catch (Exception ex) { Note($"Couldn't read “{_appleMusicName}”: {ex.Message}"); }
        finally { SetBusy(false); }
    }

    /// <summary>Adds every album in the working list to the Apple Music playlist (skips ones already there is up to Apple Music).</summary>
    private async void OnPushToAppleMusic(object? sender, RoutedEventArgs e)
    {
        if (_store is null || _store.Entries.Count == 0) return;
        if (!AppleMusic.IsAvailable) { Note("Apple Music is only available in the iPhone app."); return; }

        var albums = _store.Entries.ToList();
        SetBusy(true, "");
        int added = 0, failed = 0;
        foreach (var album in albums)
        {
            Note($"Adding {album.Title}… ({added + failed + 1}/{albums.Count})");
            var result = await AppleMusic.Writer!.AddAlbumAsync(_appleMusicName, album);
            if (result.Ok) added++; else failed++;
        }
        Note($"Pushed {added} to “{_appleMusicName}”" + (failed > 0 ? $", {failed} couldn't be added." : "."));
        SetBusy(false);
    }

    private void Note(string message)
    {
        StatusText.IsVisible = true;
        StatusText.Text = message;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        ImportButton.IsEnabled = !busy && AppleMusic.IsAvailable;
        PushButton.IsEnabled = !busy && AppleMusic.IsAvailable && (_store?.Entries.Count ?? 0) > 0;
        if (message is not null) Note(message);
    }
}
