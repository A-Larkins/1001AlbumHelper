using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        foreach (var album in _all) album.Note = "";

        ApplyFilter();
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
                if (year is not null) { album.Year = year; found++; }

                done++;

                // Batched so a long prefetch isn't also a few hundred writes.
                if (found > 0 && found % 10 == 0) Persist();

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
            var matches = await _discogs!.SearchAlbumsAsync(album.Title, album.Artist, token);
            return matches.FirstOrDefault(m => m.Year.Length > 0)?.Year;
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
