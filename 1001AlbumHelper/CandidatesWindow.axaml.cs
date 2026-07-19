using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

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

    private bool _busy;

    public CandidatesWindow()
    {
        InitializeComponent();
        RowsList.ItemsSource = _shown;
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        Opened += (_, _) => Load();
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

        // No year yet: fill it in from Discogs and stop there. Adding on the back of a lookup the
        // user never saw would quietly bake a wrong year into the sheet, so the second press
        // confirms it.
        if (year.Length == 0)
        {
            await FillYearAsync(album);
            return;
        }

        if (!int.TryParse(year, out int parsed) || parsed < 1900 || parsed > DateTime.Now.Year + 1)
        {
            album.Note = "That year doesn't look right — four digits, please.";
            return;
        }

        SetBusy(true, $"Adding “{album.Title}”…");
        album.Note = "";

        try
        {
            var result = await Operations.AddReplacementAlbumAsync(album.Title, album.Artist, parsed);
            switch (result.Outcome)
            {
                case Operations.AddOutcome.Added:
                    Decide(album, CandidateStatus.Added);
                    StatusText.Text = $"✓ “{album.Title}” added at #{result.Position} — the list was renumbered."
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

    /// <summary>
    /// Looks the album's year up on Discogs and puts it in the box without adding anything, so the
    /// user sees what they're about to commit to.
    /// </summary>
    private async Task FillYearAsync(CandidateAlbum album)
    {
        if (_discogs is null)
        {
            album.Note = "No year — type one in (Discogs lookup is off; add a token to appsettings.json).";
            return;
        }

        SetBusy(true, $"Looking up “{album.Title}” on Discogs…");
        try
        {
            var matches = await _discogs.SearchAlbumsAsync(album.Title, album.Artist);
            var hit = matches.FirstOrDefault(m => m.Year.Length > 0);

            if (hit is null)
            {
                album.Note = "Discogs didn't find a year — type one in.";
                StatusText.Text = $"No year found for “{album.Title}”.";
                return;
            }

            album.Year = hit.Year;
            album.Note = $"Discogs says {hit.Year} ({hit.Artist}) — press Keep again to add it.";
            StatusText.Text = $"Filled in {hit.Year} for “{album.Title}”. Check it, then press Keep again.";
        }
        catch (Exception ex)
        {
            album.Note = "Lookup failed — type the year in.";
            StatusText.Text = $"✗ Discogs lookup failed: {ex.Message}";
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

    private void OnReload(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _lastDropped = null;
        UndoButton.IsEnabled = false;
        Load();
        StatusText.Text = "Reloaded from disk.";
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
