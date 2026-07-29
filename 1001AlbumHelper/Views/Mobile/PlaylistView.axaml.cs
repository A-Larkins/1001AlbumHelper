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
        var active = _store.Active;
        Rows.ItemsSource = active;
        CountText.Text = active.Count == 1 ? "1 album" : $"{active.Count} albums";
        ImportButton.IsEnabled = AppleMusic.IsAvailable;
        PushButton.IsEnabled = AppleMusic.IsAvailable && active.Count > 0;

        var toRemove = _store.ToRemove;
        ToRemoveSection.IsVisible = toRemove.Count > 0;
        ToRemoveHeader.Text = $"Delete these from Apple Music ({toRemove.Count})";
        ToRemoveRows.ItemsSource = toRemove;
    }

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (_store is null || sender is not Button button || button.DataContext is not PlaylistEntry entry) return;
        _store.RequestRemoval(entry);
        Refresh();
    }

    /// <summary>The user has manually deleted this album from Apple Music — drop it off the checklist.</summary>
    private void OnConfirmRemoved(object? sender, RoutedEventArgs e)
    {
        if (_store is null || sender is not Button button || button.DataContext is not PlaylistEntry entry) return;
        _store.ConfirmRemoved(entry);
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
            int added = _store.MergeFromAppleMusic(albums);
            Refresh();
            Note($"Imported {added} new ({albums.Count} in “{_appleMusicName}”).");
        }
        catch (Exception ex) { Note($"Couldn't read “{_appleMusicName}”: {ex.Message}"); }
        finally { SetBusy(false); }
    }

    /// <summary>Adds every album in the working list to the Apple Music playlist, naming any that fail and why.</summary>
    private async void OnPushToAppleMusic(object? sender, RoutedEventArgs e)
    {
        if (_store is null || _store.Active.Count == 0) return;
        if (!AppleMusic.IsAvailable) { Note("Apple Music is only available in the iPhone app."); return; }

        var albums = _store.Active.ToList();
        SetBusy(true, "");
        int added = 0;
        var failures = new List<string>();
        foreach (var album in albums)
        {
            Note($"Adding {album.Title}… ({added + failures.Count + 1}/{albums.Count})");
            var result = await AppleMusic.Writer!.AddAlbumAsync(_appleMusicName, album);
            if (result.Ok) { _store.MarkInAppleMusic(album); added++; }
            else failures.Add($"{album.Title} — {album.Artist}: {result.Message}");
        }

        Refresh();
        Note($"Pushed {added} to “{_appleMusicName}”" + (failures.Count > 0 ? $", {failures.Count} couldn't be added (below)." : "."));
        FailuresList.ItemsSource = failures;
        FailuresList.IsVisible = failures.Count > 0;
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
        PushButton.IsEnabled = !busy && AppleMusic.IsAvailable && (_store?.Active.Count ?? 0) > 0;
        if (busy) FailuresList.IsVisible = false; // clear any failures shown from a previous push
        if (message is not null) Note(message);
    }
}
