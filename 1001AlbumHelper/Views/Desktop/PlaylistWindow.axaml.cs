using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>
/// The Mac's view of the two working playlists — the desktop counterpart of the phone's Playlist
/// tabs, and the same model: this list is the clean one the user fully controls, while the Music
/// app playlist it maps to by name is an add-only listening queue.
///
/// <para>
/// "Pull down" replaces the working list with whatever the Music app playlist currently holds;
/// "Push all" sends the working list the other way, naming any album that couldn't be added and
/// why. The two are deliberately not symmetrical — pulling is a full sync because Apple Music is
/// where the listening happens, while pushing can only add (see PROJECT.md §5). Both need
/// <see cref="AppleMusic.Writer"/>, which only macOS supplies (see MusicAppPlaylistWriter) — on
/// Windows/Linux the two buttons stay disabled and the rest of the window still works.
/// </para>
/// </summary>
public partial class PlaylistWindow : Window
{
    /// <summary>The Music app playlist each working list maps to, by name.</summary>
    private static readonly Dictionary<int, string> AppleMusicNames = new()
    {
        [1] = "PLAYLIST1",
        [2] = "PLAYLIST2",
    };

    private int _current = 1;
    private PlaylistStore? _store;
    private bool _busy;

    public PlaylistWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Show(1);
    }

    private string AppleMusicName => AppleMusicNames[_current];

    // ---------- Which playlist ----------

    private void OnPickPlaylist1(object? sender, RoutedEventArgs e) => Show(1);
    private void OnPickPlaylist2(object? sender, RoutedEventArgs e) => Show(2);

    private void Show(int id)
    {
        if (_busy) return; // don't swap the list out from under a push that's still running

        _current = id;
        Playlist1Button.Classes.Set("on", id == 1);
        Playlist2Button.Classes.Set("on", id == 2);
        FailuresList.IsVisible = false;
        StatusText.Text = "";
        Refresh();
    }

    /// <summary>Re-reads the working list from disk, so albums queued elsewhere in the app show up.</summary>
    private void Refresh()
    {
        _store = PlaylistStore.Open(_current);

        var active = _store.Active;
        RowsList.ItemsSource = active;
        EmptyText.IsVisible = active.Count == 0;
        CountText.Text = active.Count == 1 ? "1 album" : $"{active.Count} albums";

        var toRemove = _store.ToRemove;
        ToRemoveSection.IsVisible = toRemove.Count > 0;
        ToRemoveHeader.Text = $"Delete these from Apple Music ({toRemove.Count})";
        ToRemoveRows.ItemsSource = toRemove;

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool available = AppleMusic.IsAvailable;
        ImportButton.IsEnabled = !_busy && available;
        PushButton.IsEnabled = !_busy && available && (_store?.Active.Count ?? 0) > 0;
        Playlist1Button.IsEnabled = !_busy;
        Playlist2Button.IsEnabled = !_busy;

        if (!available)
        {
            ToolTip.SetTip(ImportButton, "The Music app isn't available on this platform.");
            ToolTip.SetTip(PushButton, "The Music app isn't available on this platform.");
        }
    }

    // ---------- Editing the working list ----------

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (_store is null || sender is not Button { DataContext: PlaylistEntry entry }) return;

        // Anything Apple Music already has can't be deleted through the API, so removing it here
        // moves it to the checklist instead of vanishing.
        _store.RequestRemoval(entry);
        Refresh();
    }

    private void OnConfirmRemoved(object? sender, RoutedEventArgs e)
    {
        if (_store is null || sender is not Button { DataContext: PlaylistEntry entry }) return;
        _store.ConfirmRemoved(entry);
        Refresh();
    }

    // ---------- Apple Music ----------

    /// <summary>
    /// Pulls the working list down from the Music app, which becomes the source of truth — the
    /// list ends up holding exactly what that playlist holds. See <see cref="PlaylistStore.SyncFromAppleMusic"/>.
    /// <para>
    /// On Playlist 2 the pull does one thing more: albums it finds there for the first time were
    /// queued in the Music app by hand, which is exactly what a potential replacement is, so they
    /// go on to the shortlist as well (see <see cref="ShortlistIntake"/>).
    /// </para>
    /// </summary>
    private async void OnImportFromAppleMusic(object? sender, RoutedEventArgs e)
    {
        if (_store is null || _busy) return;
        if (!AppleMusic.IsAvailable) { Note("The Music app isn't available on this platform."); return; }

        bool isRecommendations = _current == 2;
        SetBusy(true, $"Reading “{AppleMusicName}” from the Music app…");
        try
        {
            var albums = await AppleMusic.Writer!.ReadAlbumsAsync(AppleMusicName);
            var sync = _store.SyncFromAppleMusic(albums);
            string note = albums.Count == 0
                ? $"“{AppleMusicName}” is empty, or the Music app has no playlist by that name — the working list is now empty too."
                : Summarise(sync, albums.Count);

            if (isRecommendations && albums.Count > 0)
            {
                // The pulled list is already on disk, so show it before the shortlist round trip —
                // that one talks to Google Sheets and can take a moment.
                Refresh();
                Note($"{note} Checking the shortlist…");

                // The whole playlist, not just what the pull found new: see ShortlistIntake. The
                // Discogs year lookups take a second each, so they report their way down the list.
                var intake = await ShortlistIntake.AbsorbAsync(
                    albums, cacheLocally: true, progress: line => Note($"{note} {line}"));
                note = $"{note} {intake.Summary}".TrimEnd();
            }

            Note(note);
        }
        catch (Exception ex)
        {
            Note($"Couldn't read “{AppleMusicName}” — {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            Refresh();
        }
    }

    /// <summary>
    /// Adds every album on the working list to the Music app playlist, one at a time, listing the
    /// ones that failed and why rather than reporting a bare count.
    /// </summary>
    private async void OnPushToAppleMusic(object? sender, RoutedEventArgs e)
    {
        if (_store is null || _busy) return;
        if (!AppleMusic.IsAvailable) { Note("The Music app isn't available on this platform."); return; }

        var albums = _store.Active.ToList();
        if (albums.Count == 0) return;

        SetBusy(true, "");
        int added = 0;
        var failures = new List<string>();

        foreach (var album in albums)
        {
            Note($"Adding {album.Title}… ({added + failures.Count + 1}/{albums.Count})");
            var result = await AppleMusic.Writer!.AddAlbumAsync(AppleMusicName, album);
            if (result.Ok) { _store.MarkInAppleMusic(album); added++; }
            else failures.Add($"{album.Title} — {album.Artist}: {result.Message}");
        }

        Note($"Pushed {added} to “{AppleMusicName}”"
             + (failures.Count > 0 ? $", {failures.Count} couldn't be added (below)." : "."));
        FailuresList.ItemsSource = failures;
        FailuresList.IsVisible = failures.Count > 0;

        SetBusy(false);
        Refresh();
    }

    // ---------- Chrome ----------

    private void OnReload(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        Refresh();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Spells out what the pull actually changed, rather than just saying it finished.</summary>
    private static string Summarise(PlaylistSyncResult sync, int inAppleMusic)
    {
        var parts = new List<string>();
        if (sync.Added > 0) parts.Add($"{sync.Added} new");
        if (sync.Removed > 0) parts.Add($"{sync.Removed} dropped");
        if (sync.ClearedFromChecklist > 0) parts.Add($"{sync.ClearedFromChecklist} ticked off the checklist");

        string what = parts.Count > 0 ? string.Join(", ", parts) : "no changes";
        return $"Pulled {inAppleMusic} album{(inAppleMusic == 1 ? "" : "s")} — {what}.";
    }

    private void Note(string message) => StatusText.Text = message;

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        if (busy) FailuresList.IsVisible = false; // clear failures left over from a previous push
        if (message is not null) Note(message);
        UpdateButtons();
    }
}
