using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>
/// One playlist "working list" (1 = from the 1001, 2 = potential replacements). This is the clean,
/// fully add/remove list the user manages; it maps to an Apple Music playlist by name for listening.
/// "Pull down" replaces it with whatever that Apple Music playlist currently holds; "Push all" adds
/// the working list into Apple Music (add-only — Apple has no remove, so cleanup happens here in
/// the app). Pulling is a full sync, pushing can only add — see PROJECT.md §5.
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
        EmptyText.IsVisible = active.Count == 0;
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

    /// <summary>
    /// Pulls the working list down from Apple Music, which becomes the source of truth — afterwards
    /// this list holds exactly what that playlist holds. See <see cref="PlaylistStore.SyncFromAppleMusic"/>.
    /// <para>
    /// On Playlist 2 the pull does one thing more: albums it finds there for the first time were
    /// queued in Apple Music by hand, which is exactly what a potential replacement is, so they go
    /// on to the shortlist as well (see <see cref="ShortlistIntake"/>).
    /// </para>
    /// </summary>
    private async void OnImportFromAppleMusic(object? sender, RoutedEventArgs e)
    {
        if (_store is null) return;
        if (!AppleMusic.IsAvailable) { Note("Apple Music is only available in the iPhone app."); return; }

        bool isRecommendations = _playlistId == 2;
        SetBusy(true, $"Reading “{_appleMusicName}” from Apple Music…");
        try
        {
            var albums = await AppleMusic.Writer!.ReadAlbumsAsync(_appleMusicName);
            var sync = _store.SyncFromAppleMusic(albums);
            Refresh();

            string note = albums.Count == 0
                ? $"“{_appleMusicName}” is empty — so this list is now too."
                : Summarise(sync, albums.Count);

            if (isRecommendations && albums.Count > 0)
            {
                // The list itself is already up to date; the shortlist round trip talks to Google
                // Sheets, so say what's happening rather than leaving the line stale meanwhile.
                Note($"{note} Checking the shortlist…");

                // The whole playlist, not just what the pull found new: see ShortlistIntake. The
                // Discogs year lookups take a second each, so they report their way down the list.
                var intake = await ShortlistIntake.AbsorbAsync(
                    albums, cacheLocally: false, progress: line => Note($"{note} {line}"));
                note = $"{note} {intake.Summary}".TrimEnd();
            }

            Note(note);
        }
        catch (Exception ex) { Note($"Couldn't read “{_appleMusicName}”: {ex.Message}"); }
        finally { SetBusy(false); }
    }

    /// <summary>Spells out what the pull actually changed, rather than just saying it finished.</summary>
    private static string Summarise(PlaylistSyncResult sync, int inAppleMusic)
    {
        var parts = new List<string>();
        if (sync.Added > 0) parts.Add($"{sync.Added} new");
        if (sync.Removed > 0) parts.Add($"{sync.Removed} dropped");
        if (sync.ClearedFromChecklist > 0) parts.Add($"{sync.ClearedFromChecklist} ticked off");

        string what = parts.Count > 0 ? string.Join(", ", parts) : "no changes";
        return $"Pulled {inAppleMusic} — {what}.";
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
