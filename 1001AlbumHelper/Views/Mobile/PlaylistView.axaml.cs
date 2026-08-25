using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
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
    /// <summary>
    /// True on Playlist 2, the recommendations queue. A styled property rather than a field because
    /// the row template binds to it to decide whether to offer "＋ Shortlist" — Playlist 1 holds
    /// the 1001 itself, which has nothing to be shortlisted as a replacement for it.
    /// </summary>
    public static readonly StyledProperty<bool> IsRecommendationsProperty =
        AvaloniaProperty.Register<PlaylistView, bool>(nameof(IsRecommendations));

    public bool IsRecommendations
    {
        get => GetValue(IsRecommendationsProperty);
        private set => SetValue(IsRecommendationsProperty, value);
    }

    private int _playlistId;
    private string _appleMusicName = "";
    private PlaylistStore? _store;

    /// <summary>Guards against a second tap while a shortlist or keep round trip is still running.</summary>
    private bool _shortlisting;

    public PlaylistView() => InitializeComponent();

    /// <summary>
    /// Points this view at playlist <paramref name="id"/>, with a heading, the line under it, and the
    /// Apple Music playlist name. Title and subtitle are separate so the heading can stay one short
    /// phrase at heading size while "from the 1001" sits beneath it as small print.
    /// </summary>
    public void Configure(int id, string title, string appleMusicName, string subtitle)
    {
        _playlistId = id;
        _appleMusicName = appleMusicName;
        IsRecommendations = id == 2;
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
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

    /// <summary>
    /// Puts one album from Playlist 2 straight on to the potentials shortlist — the same route a
    /// pull-down takes (see <see cref="ShortlistIntake"/>), just for the one album you're listening
    /// to rather than the whole queue, so writing it down doesn't mean leaving this tab.
    /// <para>
    /// The button is deliberately stateless: rows here virtualize, so a label set on the button
    /// would follow its container on to some other album. Saying what happened in the status line
    /// costs nothing, and a second tap is harmless anyway — the intake compares against the
    /// shortlist itself, so an album already on it is left exactly as it is.
    /// </para>
    /// </summary>
    private async void OnAddToShortlist(object? sender, RoutedEventArgs e)
    {
        if (_shortlisting || sender is not Button { DataContext: PlaylistEntry entry }) return;

        _shortlisting = true;
        Note($"Adding {entry.Title} to the shortlist…");
        try
        {
            // cacheLocally: false — the phone has no writable copy of the shortlist, so the
            // Potentials sheet is the only place a candidate can be saved. Same as the pull.
            var intake = await ShortlistIntake.AbsorbAsync(
                new[] { entry }, cacheLocally: false, progress: Note);

            Note(intake.Problem is not null ? $"⚠ {intake.Problem}"
                 : intake.Added.Count > 0 ? $"✓ {entry.Title} is on the shortlist. {intake.Summary}".TrimEnd()
                 : $"{entry.Title} was already on the shortlist. {intake.Summary}".TrimEnd());
        }
        catch (Exception ex) { Note($"Couldn't add {entry.Title} to the shortlist: {ex.Message}"); }
        finally { _shortlisting = false; }
    }

    /// <summary>
    /// The decided version of the button above: puts the album on the replacements list proper and
    /// marks it kept on the shortlist, which is what the shortlist tab's own "Keep" does — see
    /// <see cref="Operations.KeepFromPlaylistAsync(PlaylistEntry, ISheetsClient, string, string, string, bool, Action{string})"/>.
    /// Both writes go through the phone's REST client, since there's no OAuth here.
    /// </summary>
    private async void OnKeep(object? sender, RoutedEventArgs e)
    {
        if (_shortlisting || sender is not Button { DataContext: PlaylistEntry entry }) return;

        var sheets = MobileSheets.Create();
        if (sheets.Client is null)
        {
            Note($"Can't reach the replacements list — Sheets sync {sheets.Status}.");
            return;
        }

        _shortlisting = true;
        Note($"Keeping {entry.Title}…");
        try
        {
            var result = await Operations.KeepFromPlaylistAsync(
                entry, sheets.Client, sheets.ReplacementsTab, sheets.AlbumsTab, sheets.StarredTab,
                cacheLocally: false, progress: Note);
            Note(result.Message);
        }
        catch (Exception ex) { Note($"Couldn't keep {entry.Title}: {ex.Message}"); }
        finally { _shortlisting = false; }
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
