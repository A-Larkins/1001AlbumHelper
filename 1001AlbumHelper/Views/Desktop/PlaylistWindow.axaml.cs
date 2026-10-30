using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>
/// One album a push couldn't add, as listed under the playlist. Top-level so the failure row
/// template can bind to it.
/// </summary>
/// <param name="OpenUrl">Its page in the Music app, when adding it there by hand would fix it.</param>
public sealed record PushFailure(string Text, string? OpenUrl)
{
    public bool CanOpen => OpenUrl is not null;
}

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

    /// <summary>
    /// True while Playlist 2 — the recommendations queue — is the one on screen. A styled property
    /// rather than a field because the row template binds to it to decide whether to offer
    /// "＋ Shortlist": Playlist 1 holds the 1001 itself, which is what the shortlist replaces.
    /// </summary>
    public static readonly StyledProperty<bool> IsRecommendationsProperty =
        AvaloniaProperty.Register<PlaylistWindow, bool>(nameof(IsRecommendations));

    public bool IsRecommendations
    {
        get => GetValue(IsRecommendationsProperty);
        private set => SetValue(IsRecommendationsProperty, value);
    }

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
        IsRecommendations = id == 2;
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

    /// <summary>
    /// Puts one album from Playlist 2 straight on to the potentials shortlist — the same route a
    /// pull-down takes (see <see cref="ShortlistIntake"/>), for the one album rather than the whole
    /// queue, so noting down what you're listening to doesn't mean a trip back to Browse.
    /// <para>
    /// The button keeps no per-row state: the list virtualizes, so a label set on it would follow
    /// its container on to another album. The status line says what happened instead, and a second
    /// press is harmless — the intake compares against the shortlist itself, so an album already on
    /// it (or already ruled out) is left exactly as it is.
    /// </para>
    /// </summary>
    private async void OnAddToShortlist(object? sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { DataContext: PlaylistEntry entry }) return;

        SetBusy(true, $"Adding {entry.Title} to the shortlist…");
        try
        {
            var intake = await ShortlistIntake.AbsorbAsync(
                new[] { entry }, cacheLocally: true, progress: Note);

            Note(intake.Problem is not null ? $"⚠ {intake.Problem}"
                 : intake.Added.Count > 0 ? $"✓ {entry.Title} is on the shortlist. {intake.Summary}".TrimEnd()
                 : $"{entry.Title} was already on the shortlist. {intake.Summary}".TrimEnd());
        }
        catch (Exception ex) { Note($"Couldn't add {entry.Title} to the shortlist — {ex.Message}"); }
        finally { SetBusy(false); }
    }

    /// <summary>
    /// The decided version of the button above, and the same thing the shortlist window's "Keep"
    /// does: on to the replacements list proper — slotted into its year block and renumbered — and
    /// marked kept on the shortlist. See
    /// <see cref="Operations.KeepFromPlaylistAsync(PlaylistEntry, Action{string})"/>.
    /// </summary>
    private async void OnKeep(object? sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { DataContext: PlaylistEntry entry }) return;

        SetBusy(true, $"Keeping {entry.Title}…");
        try
        {
            var result = await Operations.KeepFromPlaylistAsync(entry, progress: Note);
            Note(result.Message);
        }
        catch (Exception ex) { Note($"Couldn't keep {entry.Title} — {ex.Message}"); }
        finally { SetBusy(false); }
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
        var failures = new List<PushFailure>();

        foreach (var album in albums)
        {
            Note($"Adding {album.Title}… ({added + failures.Count + 1}/{albums.Count})");
            var result = await AppleMusic.Writer!.AddAlbumAsync(AppleMusicName, album);
            if (result.Ok) { _store.MarkInAppleMusic(album); added++; }
            else failures.Add(new PushFailure($"{album.Title} — {album.Artist}: {result.Message}", result.OpenUrl));
        }

        Note($"Pushed {added} to “{AppleMusicName}”"
             + (failures.Count > 0 ? $", {failures.Count} couldn't be added (below)." : "."));
        FailuresList.ItemsSource = failures;
        FailuresList.IsVisible = failures.Count > 0;

        SetBusy(false);
        Refresh();
    }

    /// <summary>
    /// Opens a failed album's page in the Music app, where ＋ adds it to the library — the one step
    /// AppleScript can't take. The row stays put: the next push is what confirms it worked.
    /// </summary>
    private async void OnOpenInMusic(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PushFailure { OpenUrl: { } url } }) return;
        if (!await Launcher.LaunchUriAsync(new Uri(url)))
            Note("Couldn't open the Music app.");
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
