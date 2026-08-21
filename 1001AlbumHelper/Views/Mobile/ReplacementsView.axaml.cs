using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>
/// Mobile browser over the potential-replacements shortlist: search, add new candidates (with
/// Discogs year lookup), edit a year in place, add a row to Playlist 2, and rule on a candidate —
/// Keep puts it on the replacements list, Nah drops it for good. Every one of those needs live
/// Sheets sync: there's no writable local cache on the phone, so a change that couldn't reach Sheets
/// would vanish the moment the view reloads.
/// <para>
/// Keep writes to the *master* spreadsheet (via <see cref="MobileSheets"/>), while the shortlist
/// itself lives on the Potentials tab (via <see cref="CandidateRepository"/>) — two different sheets,
/// which is why this view holds both.
/// </para>
/// </summary>
public partial class ReplacementsView : UserControl
{
    private List<CandidateAlbum> _all = new();
    private bool _isLive;   // true once the list came from Google Sheets (not the baked-in snapshot)
    private readonly PlaylistStore _playlist2 = PlaylistStore.Open(2);
    private readonly CandidateRepository _repo = CandidateRepository.Create();
    private CandidateSortColumn? _sortColumn;
    private bool _sortDescending;

    private bool _busy;
    private CandidateAlbum? _lastDropped;

    // Built on the first Keep rather than at load: nothing else here touches the master spreadsheet.
    private MobileSheets? _sheets;

    // Null when no Discogs token is configured: adding still works, just without lookup.
    private readonly DiscogsApiClient? _discogs = DiscogsApiClient.TryCreate();

    public ReplacementsView()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        Loaded += (_, _) => Load(fromSnapshot: true);

        LookupHint.Text = AlbumLookup.Attach(
            _discogs, TitleBox, ArtistBox, YearBox,
            pick => { LookupHint.Text = AlbumLookup.Picked(pick); AddStatusText.Text = ""; });

        UpdateSortIndicators();
    }

    /// <summary>
    /// Re-reads the shortlist when the tab comes forward, so candidates added elsewhere — a Playlist 2
    /// pull, or the Mac — are here rather than a screenful of what the list held when the app started.
    /// Held off while a decision is in flight, which is the one time the list on screen is the truth.
    /// </summary>
    public void Refresh()
    {
        if (_busy) return;
        Load(fromSnapshot: false);
    }

    /// <param name="fromSnapshot">
    /// True on first load, where the baked-in snapshot fills the tab instantly and Sheets replaces it
    /// a moment later. False on a refresh: the live list is already on screen, and dropping back to a
    /// build-time snapshot in front of the user would be a step backwards, not a step faster.
    /// </param>
    private async void Load(bool fromSnapshot)
    {
        // Instant: the snapshot baked into the app.
        if (fromSnapshot)
        {
            try { SetAll(MobileData.LoadCandidates()); }
            catch (Exception ex) { CountText.Text = $"Couldn't load candidates: {ex.Message}"; }
        }

        // Then: the live shared list from Google Sheets. Show exactly what happened either way.
        if (!_repo.SyncEnabled)
        {
            SyncText.Text = $"⚠ Sheets sync {_repo.Status} — browsing only, nothing here can be saved.";
            AddToggle.IsEnabled = false;
            return;
        }

        SyncText.Text = "Syncing with Google Sheets…";
        try
        {
            var live = await _repo.PullAsync();
            if (live is not null)
            {
                _isLive = true;
                SetAll(live);
                // The count line below already says "· live ✓" once synced — no need to repeat it here.
                SyncText.Text = "";
            }
        }
        catch (Exception ex)
        {
            SyncText.Text = $"⚠ Sync failed — {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>Swaps in a fresh candidate list, watching each album's Year so an in-place edit pushes to Sheets.</summary>
    private void SetAll(List<CandidateAlbum> albums)
    {
        foreach (var album in _all) album.PropertyChanged -= OnAlbumChanged;
        _all = albums;
        foreach (var album in _all) album.PropertyChanged += OnAlbumChanged;
        ApplyFilter();
    }

    private void OnAlbumChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CandidateAlbum.Year)) PushToSheets();
    }

    /// <summary>Pushes the shortlist to Google Sheets in the background (no-op when sync is off).</summary>
    private async void PushToSheets()
    {
        if (!_repo.SyncEnabled) return;
        try { await _repo.PushAsync(_all); }
        catch (Exception ex) { SyncText.Text = $"⚠ Saved here, but Sheets sync failed: {ex.Message}"; }
    }

    private void ApplyFilter()
    {
        // Only undecided albums are on offer — kept and dropped ones stay in the file (so a decision
        // sticks and they're never offered again) but have no business in the list. Same as desktop.
        var pending = _all.Where(a => a.Status == CandidateStatus.Pending).ToList();

        string query = SearchBox.Text?.Trim() ?? "";
        List<CandidateAlbum> shown;
        if (query.Length == 0)
        {
            shown = pending;
        }
        else
        {
            var terms = NumberedList.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            shown = pending.Where(a =>
            {
                string hay = $"{NumberedList.Normalize(a.Title)} {NumberedList.Normalize(a.Artist)}";
                return terms.All(t => hay.Contains(t, StringComparison.Ordinal));
            }).ToList();
        }

        if (_sortColumn is { } column) shown = ReplacementCandidates.Sort(shown, column, _sortDescending);

        Rows.ItemsSource = shown;
        string count = shown.Count == pending.Count
            ? $"{pending.Count} candidates"
            : $"{shown.Count} of {pending.Count} candidates";
        CountText.Text = _isLive ? $"{count} · live ✓" : count;
    }

    // ---------- Sorting ----------

    /// <summary>Tap a column to sort by it ascending; tap the active one again to flip direction.</summary>
    private void OnSortHeader(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse(tag, out CandidateSortColumn column))
            return;

        if (_sortColumn == column) _sortDescending = !_sortDescending;
        else { _sortColumn = column; _sortDescending = false; }

        UpdateSortIndicators();
        ApplyFilter();
    }

    private void UpdateSortIndicators()
    {
        (CandidateSortColumn Column, Button Button, string Label)[] buttons =
        {
            (CandidateSortColumn.Title, SortTitleButton, "Title"),
            (CandidateSortColumn.Artist, SortArtistButton, "Artist"),
            (CandidateSortColumn.Genre, SortGenreButton, "Genre"),
            (CandidateSortColumn.Year, SortYearButton, "Year"),
        };

        foreach (var (column, button, label) in buttons)
        {
            bool active = _sortColumn == column;
            button.Classes.Set("on", active);
            button.Content = active ? $"{label} {(_sortDescending ? "▼" : "▲")}" : label;
        }
    }

    private void OnAddToPlaylist2(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not CandidateAlbum album) return;
        bool added = _playlist2.Add(album.Title, album.Artist, album.Year);
        button.Content = added ? "✓ P2" : "· P2";
        button.IsEnabled = false;
    }

    // ---------- Deciding: Keep / Nah ----------

    private async void OnKeepRow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CandidateAlbum album }) return;
        await KeepAsync(album);
    }

    /// <summary>
    /// Puts the album on the replacements list — slotted into its year block, renumbering the sheet —
    /// and takes it off the shortlist. Mirrors the desktop's Keep (see CandidatesWindow.KeepAsync).
    /// </summary>
    private async Task KeepAsync(CandidateAlbum album)
    {
        if (_busy) return;
        if (!RequireSync()) return;

        string year = album.Year.Trim();
        string? lookedUp = null;

        // No year typed in: look it up and carry straight on, rather than making them press twice.
        if (year.Length == 0)
        {
            SetBusy(true, $"Looking up “{album.Title}” on Discogs…");
            try { lookedUp = await LookUpYearAsync(album); }
            finally { SetBusy(false); }

            if (lookedUp is null)
            {
                album.Note = _discogs is null
                    ? "No year — type one in (Discogs lookup isn't set up on this device)."
                    : "Discogs didn't find a year — type one in.";
                Status("");
                return;
            }

            album.Year = lookedUp;   // persists via the Year watcher in SetAll
            year = lookedUp;
        }

        if (!int.TryParse(year, out int parsed) || parsed < 1900 || parsed > DateTime.Now.Year + 1)
        {
            album.Note = "That year doesn't look right — four digits, please.";
            return;
        }

        _sheets ??= MobileSheets.Create();
        if (_sheets.Client is null)
        {
            Status($"✗ Can't reach the replacements list — Sheets sync {_sheets.Status}.");
            return;
        }

        SetBusy(true, $"Adding “{album.Title}” ({parsed})…");
        album.Note = "";

        try
        {
            var result = await Operations.AddReplacementAlbumAsync(
                _sheets.Client, _sheets.ReplacementsTab, _sheets.AlbumsTab, _sheets.StarredTab,
                album.Title, album.Artist, parsed);

            switch (result.Outcome)
            {
                case Operations.AddOutcome.Added:
                    Decide(album, CandidateStatus.Added);
                    // Name the year when it was looked up rather than seen — it's the one part of
                    // the row they didn't get a chance to eye before it went in.
                    Status($"✓ “{album.Title}” added at #{result.Position} — the list was renumbered."
                           + (lookedUp is null ? "" : $" Year {parsed} came from Discogs.")
                           + (result.Warning is null ? "" : $"\n{result.Warning}"));
                    break;

                case Operations.AddOutcome.AlreadyInReplacements:
                    // Already where we wanted it: nothing to do, so take it off the shortlist.
                    Decide(album, CandidateStatus.Added);
                    Status($"“{album.Title}” was already there — taken off the shortlist. {result.Detail}");
                    break;

                case Operations.AddOutcome.AlreadyIn1001:
                    // Left in place: it's the user's call whether that 1001 entry is really this album.
                    album.Note = result.Detail ?? "Already on the 1001 list.";
                    Status($"⚠ Not added — “{album.Title}” is already on the 1001 list. Drop it with Nah if that's the same album.");
                    break;

                case Operations.AddOutcome.NotConfigured:
                    Status($"✗ {result.Detail}");
                    break;

                default:
                    album.Note = result.Detail ?? "";
                    Status($"✗ Couldn't add “{album.Title}”.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Status($"✗ {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnDropRow(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (sender is not Button { DataContext: CandidateAlbum album }) return;
        if (!RequireSync()) return;

        Decide(album, CandidateStatus.Declined);
        _lastDropped = album;
        UndoButton.IsVisible = true;
        Status($"Dropped “{album.Title}” — it won't be offered again.");
    }

    private void OnUndo(object? sender, RoutedEventArgs e)
    {
        if (_busy || _lastDropped is null) return;

        var album = _lastDropped;
        _lastDropped = null;
        UndoButton.IsVisible = false;

        Decide(album, CandidateStatus.Pending);
        Status($"“{album.Title}” is back on the shortlist.");
    }

    /// <summary>Records a decision and pushes it up, so the album stops being offered on both devices.</summary>
    private void Decide(CandidateAlbum album, CandidateStatus status)
    {
        album.Status = status;
        album.Note = "";
        PushToSheets();
        ApplyFilter();
    }

    /// <summary>The album's year according to Discogs, or null if lookup is off or found nothing.</summary>
    private async Task<string?> LookUpYearAsync(CandidateAlbum album)
    {
        if (_discogs is null) return null;
        try { return (await _discogs.FindAlbumAsync(album.Title, album.Artist))?.Year; }
        catch { return null; }   // One album failing to resolve isn't worth an error message.
    }

    /// <summary>
    /// Decisions are only offered when they can be saved: with no sync there's nowhere on the phone
    /// to keep them, so the album would silently come back on the next load.
    /// </summary>
    private bool RequireSync()
    {
        if (_repo.SyncEnabled) return true;
        Status($"✗ Sheets sync {_repo.Status} — a decision made here couldn't be saved.");
        return false;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        if (message is not null) Status(message);
    }

    private void Status(string message)
    {
        StatusText.Text = message;
        StatusText.IsVisible = message.Length > 0;
    }

    // ---------- Add to shortlist ----------

    private void OnToggleAdd(object? sender, RoutedEventArgs e)
    {
        bool showing = !AddPanel.IsVisible;
        AddPanel.IsVisible = showing;
        AddToggle.Content = showing ? "▲ Add album" : "＋ Add album";
        if (showing) TitleBox.Focus();
    }

    private void OnAddCandidate(object? sender, RoutedEventArgs e)
    {
        string title = TitleBox.Text?.Trim() ?? "";
        string artist = ArtistBox.Text?.Trim() ?? "";
        string year = YearBox.Text?.Trim() ?? "";

        if (title.Length == 0) { AddFail("Enter the album title."); return; }
        if (artist.Length == 0) { AddFail("Enter the artist."); return; }
        if (year.Length > 0 && (!int.TryParse(year, out int y) || y < 1900 || y > DateTime.Now.Year + 1))
        {
            AddFail("That year doesn't look right — four digits, or leave it blank.");
            return;
        }

        var (outcome, match) = ReplacementCandidates.Classify(_all, title, artist);
        CandidateAlbum album;
        switch (outcome)
        {
            case CandidateAddOutcome.AlreadyPending:
                AddFail($"“{match!.Title}” by {match.Artist} is already on the shortlist, waiting to be decided on.");
                return;
            case CandidateAddOutcome.AlreadyKept:
                AddFail($"“{match!.Title}” has already been kept — it's on your replacements list.");
                return;
            case CandidateAddOutcome.Reopen:
                album = match!;
                album.Status = CandidateStatus.Pending;
                album.Note = "";
                break;
            default:
                album = new CandidateAlbum { Title = title, Artist = artist, Year = year, Status = CandidateStatus.Pending };
                _all.Add(album);
                album.PropertyChanged += OnAlbumChanged;
                break;
        }

        PushToSheets();
        TitleBox.Text = "";
        ArtistBox.Text = "";
        YearBox.Text = "";
        SearchBox.Text = ""; // clear the filter, or the new/reopened row may be hidden by it
        ApplyFilter();

        AddStatusText.Text = outcome == CandidateAddOutcome.Reopen
            ? $"“{album.Title}” is back on the shortlist."
            : $"Added “{album.Title}” to the shortlist"
              + (album.Year.Length > 0 ? $" ({album.Year})." : " — enter a year when you know it.");
    }

    private void AddFail(string message) => AddStatusText.Text = $"✗ {message}";
}
