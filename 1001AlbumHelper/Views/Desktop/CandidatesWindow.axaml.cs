using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace _1001AlbumHelper;

/// <summary>
/// The shortlist of albums that might join the replacements list: one row each, kept or dropped
/// one at a time.
/// <para>
/// Keeping an album routes through <see cref="Operations.AddReplacementAlbumAsync"/>, so it lands
/// in its year block and the sheet is renumbered — the same path the add-album dialog uses, with
/// the same refusals when the album is already on one of the lists.
/// </para>
/// </summary>
public partial class CandidatesWindow : Window
{
    /// <summary>Every album in the file, decided or not — this is what gets written back.</summary>
    private List<CandidateAlbum> _all = new();

    /// <summary>Just the pending ones, filtered by the search box. Bound to the list.</summary>
    private readonly ObservableCollection<CandidateAlbum> _shown = new();

    /// <summary>The last album dropped, so a mis-click is one button away from being undone.</summary>
    private CandidateAlbum? _lastDropped;

    /// <summary>Which column the list is sorted by, or null for the file's own order.</summary>
    private CandidateSortColumn? _sortColumn;
    private bool _sortDescending;

    // Null when no Discogs token is configured: years then have to be typed in by hand.
    private readonly DiscogsApiClient? _discogs = DiscogsApiClient.TryCreate();

    /// <summary>Stops the background year lookup when the window closes.</summary>
    private readonly CancellationTokenSource _closing = new();

    private bool _busy;
    private bool _prefetching;

    /// <summary>
    /// Discogs allows 60 requests a minute against a token, so the sustained ceiling is one a
    /// second however the work is arranged. Spacing them a shade wider than that keeps the
    /// prefetch clear of a 429, which would otherwise read as "no year found" and stick.
    /// </summary>
    private static readonly TimeSpan LookupSpacing = TimeSpan.FromMilliseconds(1100);

    public CandidatesWindow()
    {
        InitializeComponent();
        RowsList.ItemsSource = _shown;
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        Opened += async (_, _) =>
        {
            // Rows come from the local file, so the modal is populated and interactive straight
            // away. Only then does the year fetch start, behind the ticker — yielding at
            // Background priority so the first paint is finished rather than merely likely.
            Load();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await PrefetchYearsAsync();
        };
        Closed += (_, _) => _closing.Cancel();
    }

    // ---------- Loading ----------
    private void Load()
    {
        // Reloading rebuilds the list, so drop the old subscriptions or a year edited after two
        // reloads would write the file three times.
        foreach (var album in _all) album.PropertyChanged -= OnAlbumChanged;

        try
        {
            _all = ReplacementCandidates.Load();
        }
        catch (Exception ex)
        {
            _all = new List<CandidateAlbum>();
            ShowMessage($"Couldn't read the shortlist.\n\n{ex.Message}\n\n{ReplacementCandidates.FilePath}");
            return;
        }

        // A note is per-attempt feedback, not a fact about the album — start every session clean.
        foreach (var album in _all)
        {
            album.Note = "";
            Watch(album);
        }

        ApplyFilter();
    }

    /// <summary>
    /// Persists whenever an album's year changes, from wherever it changed.
    /// <para>
    /// A year typed into a row updates the album through its binding and nothing more, so without
    /// this it would live only as long as the window: closing or reloading would lose it, and the
    /// next prefetch would go and ask Discogs for a year the user had already supplied. Saving
    /// centrally means every route to a year — typed, fetched, corrected — is durable by default
    /// rather than each one having to remember.
    /// </para>
    /// </summary>
    private void Watch(CandidateAlbum album) => album.PropertyChanged += OnAlbumChanged;

    private void OnAlbumChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CandidateAlbum.Year)) Persist();
    }

    // ---------- Filtering ----------
    private void ApplyFilter()
    {
        var pending = _all.Where(a => a.Status == CandidateStatus.Pending).ToList();

        string query = SearchBox.Text?.Trim() ?? "";
        List<CandidateAlbum> shown;

        if (query.Length == 0)
        {
            shown = pending;
        }
        else
        {
            // Same rule as the browser: every term has to appear, so two words narrow.
            var terms = NumberedList.Normalize(query)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            shown = pending.Where(a =>
            {
                string hay = NumberedList.Normalize($"{a.Title} {a.Artist} {a.Genre} {a.Year}");
                return terms.All(t => hay.Contains(t, StringComparison.Ordinal));
            }).ToList();
        }

        // Sorting is a view concern only: _all stays in file order, which is the order the prefetch
        // fills years in and the order rows are decided in by default.
        if (_sortColumn is { } column)
            shown = ReplacementCandidates.Sort(shown, column, _sortDescending);

        _shown.Clear();
        foreach (var album in shown) _shown.Add(album);

        int decided = _all.Count - pending.Count;
        CountText.Text = shown.Count == pending.Count
            ? $"{pending.Count} to decide" + (decided > 0 ? $" · {decided} decided" : "")
            : $"{shown.Count} of {pending.Count} to decide";

        MessageText.IsVisible = shown.Count == 0;
        if (shown.Count == 0)
        {
            MessageText.Text = pending.Count == 0
                ? _all.Count == 0
                    ? $"No shortlist yet.\n\nAdd albums to {ReplacementCandidates.FileName} beside the app."
                    : "Every album on the shortlist has been decided. 🎉"
                : $"Nothing matches “{query}”.";
        }
    }

    // ---------- Sorting ----------

    /// <summary>
    /// A column heading was clicked: sort by it ascending, or flip the direction if it was already
    /// the sort column. The choice sticks across filtering and reloads until another heading is
    /// picked — it's a view preference, not something written to the file.
    /// </summary>
    private void OnSortHeader(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !Enum.TryParse(tag, out CandidateSortColumn column)) return;

        if (_sortColumn == column) _sortDescending = !_sortDescending;
        else { _sortColumn = column; _sortDescending = false; }

        UpdateSortIndicators();
        ApplyFilter();
    }

    /// <summary>Marks the active heading and points its arrow the way the column is running.</summary>
    private void UpdateSortIndicators()
    {
        (CandidateSortColumn Column, Button Header, TextBlock Arrow)[] headers =
        {
            (CandidateSortColumn.Title, HeaderTitle, ArrowTitle),
            (CandidateSortColumn.Artist, HeaderArtist, ArrowArtist),
            (CandidateSortColumn.Genre, HeaderGenre, ArrowGenre),
            (CandidateSortColumn.Year, HeaderYear, ArrowYear),
        };

        foreach (var (column, header, arrow) in headers)
        {
            bool active = _sortColumn == column;
            header.Classes.Set("active", active);
            arrow.Text = active ? (_sortDescending ? "▼" : "▲") : "";
        }
    }

    // ---------- Year lookup ----------

    /// <summary>
    /// Fills in the missing years from Discogs in the background, top down, saving as it goes.
    /// <para>
    /// Runs on the UI thread — every await here is network or a timer, so nothing is blocked — and
    /// works in list order because that's the order they'll be decided in: by the time the first
    /// few rows have been ruled on, the ones below have their years. The results are written to the
    /// shortlist file, so this is a one-time cost rather than something paid on every open.
    /// </para>
    /// </summary>
    private async Task PrefetchYearsAsync()
    {
        // A reload during a prefetch would otherwise start a second loop and double the request
        // rate, which is exactly what the spacing exists to avoid.
        if (_discogs is null || _prefetching) return;

        var missing = _all
            .Where(a => a.Status == CandidateStatus.Pending && a.Year.Trim().Length == 0)
            .ToList();

        // Nothing to do is the normal case once the years have been cached — claim the flag and
        // start the spinner only past this point, or that path would leave both stuck on.
        if (missing.Count == 0) return;

        _prefetching = true;
        LookupSpinner.IsVisible = true;

        int done = 0, found = 0;
        var token = _closing.Token;

        try
        {
            foreach (var album in missing)
            {
                if (token.IsCancellationRequested) break;

                // The user may have typed one in while this was working down the list.
                if (album.Year.Trim().Length > 0) { done++; continue; }

                LookupText.Text = $"Looking up years… {done + 1}/{missing.Count}";

                var year = await LookUpYearAsync(album, token);
                // Setting the year is what saves it — see Watch. A year costs a second of someone
                // else's rate limit to fetch and nothing to keep, so quitting mid-run loses none
                // of what's already resolved.
                if (year is not null) { album.Year = year; found++; }

                done++;

                await Task.Delay(LookupSpacing, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Window closed mid-prefetch: keep whatever was resolved.
        }
        finally
        {
            _prefetching = false;
            LookupSpinner.IsVisible = false;
            if (found > 0) Persist();
            if (!token.IsCancellationRequested)
            {
                int stillMissing = _all.Count(a =>
                    a.Status == CandidateStatus.Pending && a.Year.Trim().Length == 0);
                LookupText.Text = stillMissing == 0
                    ? ""
                    : $"{stillMissing} without a year — type those in";
            }
        }
    }

    /// <summary>The album's release year according to Discogs, or null if it couldn't be found.</summary>
    private async Task<string?> LookUpYearAsync(CandidateAlbum album, CancellationToken token)
    {
        try
        {
            return (await _discogs!.FindAlbumAsync(album.Title, album.Artist, token))?.Year;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null; // One album failing to resolve isn't a reason to stop the rest.
        }
    }

    // ---------- Keep ----------
    private async void OnKeepRow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CandidateAlbum album }) return;
        await KeepAsync(album);
    }

    private async Task KeepAsync(CandidateAlbum album)
    {
        if (_busy) return;

        string year = album.Year.Trim();
        string? lookedUp = null;

        // Usually already filled in by the prefetch. This covers a row reached before the prefetch
        // got to it: look it up now and carry straight on, rather than making them press twice.
        if (year.Length == 0)
        {
            SetBusy(true, $"Looking up “{album.Title}” on Discogs…");
            try
            {
                lookedUp = await LookUpYearAsync(album, _closing.Token);
            }
            finally
            {
                SetBusy(false);
            }

            if (lookedUp is null)
            {
                album.Note = _discogs is null
                    ? "No year — type one in (Discogs lookup is off; add a token to appsettings.json)."
                    : "Discogs didn't find a year — type one in.";
                return;
            }

            album.Year = lookedUp;
            year = lookedUp;
        }

        if (!int.TryParse(year, out int parsed) || parsed < 1900 || parsed > DateTime.Now.Year + 1)
        {
            album.Note = "That year doesn't look right — four digits, please.";
            return;
        }

        SetBusy(true, $"Adding “{album.Title}” ({parsed})…");
        album.Note = "";

        try
        {
            var result = await Operations.AddReplacementAlbumAsync(album.Title, album.Artist, parsed);
            switch (result.Outcome)
            {
                case Operations.AddOutcome.Added:
                    Decide(album, CandidateStatus.Added);
                    // Name the year when it was looked up rather than seen — it's the one part of
                    // the row they didn't get a chance to eye before it went in.
                    StatusText.Text = $"✓ “{album.Title}” added at #{result.Position} — the list was renumbered."
                                    + (lookedUp is null ? "" : $" Year {parsed} came from Discogs.")
                                    + (result.Warning is null ? "" : $"\n{result.Warning}");
                    break;

                case Operations.AddOutcome.AlreadyInReplacements:
                    // Already where we wanted it: nothing to do, so take it off the shortlist.
                    Decide(album, CandidateStatus.Added);
                    StatusText.Text = $"“{album.Title}” was already there — taken off the shortlist. {result.Detail}";
                    break;

                case Operations.AddOutcome.AlreadyIn1001:
                    // Left in place: it's the user's call whether that 1001 entry is really this album.
                    album.Note = result.Detail ?? "Already on the 1001 list.";
                    StatusText.Text = $"⚠️ Not added — “{album.Title}” is already on the 1001 list. Drop it with Nah if that's the same album.";
                    break;

                case Operations.AddOutcome.NotConfigured:
                    StatusText.Text = $"✗ {result.Detail}";
                    break;

                default:
                    album.Note = result.Detail ?? "";
                    StatusText.Text = $"✗ Couldn't add “{album.Title}”.";
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"✗ {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---------- Drop ----------
    private void OnDropRow(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (sender is not Button { DataContext: CandidateAlbum album }) return;

        Decide(album, CandidateStatus.Declined);
        _lastDropped = album;
        UndoButton.IsEnabled = true;
        StatusText.Text = $"Dropped “{album.Title}” — it won't be offered again.";
    }

    // ---------- Adding a potential ----------
    private async void OnAddCandidate(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var form = new AddCandidateWindow(_all);
        await form.ShowDialog(this);

        var album = form.Result;
        if (album is not null)
        {
            _all.Add(album);
            Watch(album);
        }
        else if (form.Reopened is not null)
        {
            album = form.Reopened;
            album.Status = CandidateStatus.Pending;
            album.Note = "";
        }
        else
        {
            return; // Cancelled.
        }

        Persist();
        SearchBox.Text = "";   // Clear the filter, or the new row may be hidden by it.
        ApplyFilter();

        // Appended to the end of a long list, so show the user where it went.
        RowsList.ScrollIntoView(album);
        RowsList.SelectedItem = album;

        StatusText.Text = form.Reopened is not null
            ? $"“{album.Title}” is back on the shortlist."
            : $"Added “{album.Title}” to the shortlist"
              + (album.Year.Length > 0 ? $" ({album.Year})." : " — its year will be looked up.");

        // A new row with no year is exactly what the prefetch exists for.
        await PrefetchYearsAsync();
    }

    private void OnUndo(object? sender, RoutedEventArgs e)
    {
        if (_busy || _lastDropped is null) return;

        var album = _lastDropped;
        album.Status = CandidateStatus.Pending;
        album.Note = "";
        _lastDropped = null;
        UndoButton.IsEnabled = false;

        Persist();
        ApplyFilter();
        StatusText.Text = $"“{album.Title}” is back on the shortlist.";
    }

    /// <summary>Records a decision, writes it to disk, and drops the row out of the list.</summary>
    private void Decide(CandidateAlbum album, CandidateStatus status)
    {
        album.Status = status;
        album.Note = "";
        Persist();
        ApplyFilter();
    }

    private void Persist()
    {
        try
        {
            ReplacementCandidates.Save(_all);
        }
        catch (Exception ex)
        {
            // The sheet has already been written by this point, so say so rather than pretending
            // the decision stuck — the album would otherwise be offered again next time.
            StatusText.Text = $"⚠️ Couldn't save the shortlist: {ex.Message}";
        }
    }

    // ---------- Chrome ----------
    private void SetBusy(bool on, string? message = null)
    {
        _busy = on;
        BusySpinner.IsVisible = on;
        RowsList.IsEnabled = !on;
        SearchBox.IsEnabled = !on;
        ReloadButton.IsEnabled = !on;
        AddCandidateButton.IsEnabled = !on;
        UndoButton.IsEnabled = !on && _lastDropped is not null;
        if (message is not null) StatusText.Text = message;
    }

    private void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessageText.IsVisible = true;
        _shown.Clear();
        CountText.Text = "";
    }

    private async void OnReload(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _lastDropped = null;
        UndoButton.IsEnabled = false;
        Load();
        StatusText.Text = "Reloaded from disk.";

        // Picks up years for anything newly added to the file by hand.
        await PrefetchYearsAsync();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
