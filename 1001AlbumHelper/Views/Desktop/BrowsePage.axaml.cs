using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>Read-only browser over the three lists, with a search box.</summary>
public partial class BrowsePage : UserControl, IPage
{
    private enum Which { Master, MustHear, Replacements }

    private readonly Dictionary<Which, List<ViewRow>> _cache = new();
    private readonly PlaylistStore _playlist1 = PlaylistStore.Open(1);
    private readonly PlaylistStore _playlist2 = PlaylistStore.Open(2);
    private Which _current = Which.Master;

    /// <summary>
    /// Set when the 1001 tab is (re)opened, so the list lands on the next album to rate instead of
    /// on #1. Spent by the next unfiltered showing, so re-rating a row or typing a search never
    /// yanks the list away from where the user is looking.
    /// </summary>
    private bool _jumpToResume = true;

    public BrowsePage()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => ApplyFilter();
    }

    private bool _loaded;

    public async void OnShown()
    {
        if (_loaded) return;
        _loaded = true;
        await LoadAsync();
    }

    /// <summary>
    /// Folds ratings made elsewhere — on the Rate page — into the rows here, so a switch to Browse
    /// shows them without re-reading the sheet. Keyed by sheet row, as the rater reports them.
    /// </summary>
    public void ApplyRatings(IReadOnlyDictionary<int, string> ratings)
    {
        if (ratings.Count == 0 || !_cache.TryGetValue(Which.Master, out var master)) return;
        foreach (var row in master)
            if (ratings.TryGetValue(row.SheetRow, out var rating)) row.Rating = rating;
        _cache.Remove(Which.MustHear); // a ⭐ given or taken rewrites it
    }

    // ---------- Loading ----------
    private async Task LoadAsync(bool force = false)
    {
        if (force) _cache.Clear();
        if (_cache.Count > 0) { Show(_current); return; }

        ShowMessage("Loading lists from Google Sheets…");
        SetButtonsEnabled(false);

        try
        {
            var loaded = await Task.Run(Operations.LoadAllListsAsync);
            if (loaded.Error is not null)
            {
                ShowMessage(loaded.Error);
                SetButtonsEnabled(true);
                return;
            }

            _cache[Which.Master] = loaded.Master
                .Select(a => new ViewRow(a.Number, a.Rating, a.Title, a.Artist, a.Year,
                                         sheetRow: a.SheetRow)).ToList();
            _cache[Which.MustHear] = loaded.MustHear
                .Select(r => new ViewRow(r.Number, "", r.Title, r.Artist, r.Year)).ToList();
            _cache[Which.Replacements] = loaded.Replacements
                .Select(r => new ViewRow(r.Number, "", r.Title, r.Artist, r.Year, playlist: 2)).ToList();

            SetButtonsEnabled(true);
            Show(_current);
        }
        catch (Exception ex)
        {
            ShowMessage($"Couldn't load the lists.\n\n{ex.Message}");
            SetButtonsEnabled(true);
        }
    }

    // ---------- List selection ----------
    private void OnPickMaster(object? sender, RoutedEventArgs e) => Show(Which.Master);
    private void OnPickMustHear(object? sender, RoutedEventArgs e) => Show(Which.MustHear);
    private void OnPickReplacements(object? sender, RoutedEventArgs e) => Show(Which.Replacements);

    private async void Show(Which which)
    {
        if (which == Which.Master && _current != Which.Master) _jumpToResume = true;
        _current = which;
        MasterButton.Classes.Set("on", which == Which.Master);
        MustHearButton.Classes.Set("on", which == Which.MustHear);
        ReplacementsButton.Classes.Set("on", which == Which.Replacements);

        // Only the master list carries ratings.
        RatingHeader.Text = which == Which.Master ? "RATING" : "";

        // A list can be missing because rating dropped it as stale (see OnRate) — fetch it again
        // rather than leaving the previous list on screen under the new tab's heading.
        if (!_cache.ContainsKey(which)) { await LoadAsync(force: true); return; }

        ApplyFilter();
    }

    // ---------- Filtering ----------
    private void ApplyFilter()
    {
        if (!_cache.TryGetValue(_current, out var all)) return;

        string query = SearchBox.Text?.Trim() ?? "";
        List<ViewRow> shown;

        if (query.Length == 0)
        {
            shown = all;
        }
        else
        {
            // Every whitespace-separated term must appear somewhere in the row, so
            // "nirvana 1993" narrows rather than widening.
            var terms = NumberedList.Normalize(query)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            shown = all.Where(r =>
            {
                string hay = r.Haystack.ToLowerInvariant();
                return terms.All(t => hay.Contains(t, StringComparison.Ordinal));
            }).ToList();
        }

        RowsList.ItemsSource = shown;

        if (_jumpToResume && _current == Which.Master && query.Length == 0 && shown.Count > 0)
        {
            _jumpToResume = false;
            ScrollToResume(shown);
        }

        string name = _current switch
        {
            Which.Master => "the 1001",
            Which.MustHear => "Must Hear",
            _ => "replacements",
        };
        CountText.Text = shown.Count == all.Count
            ? $"{all.Count} in {name}"
            : $"{shown.Count} of {all.Count} in {name}";

        MessageText.IsVisible = shown.Count == 0;
        if (shown.Count == 0)
            MessageText.Text = all.Count == 0 ? "This list is empty." : $"Nothing matches “{query}”.";

        StatusText.Text = _current == Which.Master
            ? "Ratings: ⭐ favourite · 👍 good · 👎 poor · ❌ trash · ✓ listened, unrated"
            : "";
    }

    /// <summary>
    /// Brings the next album to rate to the top of the list, with a couple of rated rows above it
    /// for context. ScrollIntoView only scrolls as far as it must — coming from the top that leaves
    /// the row on the bottom edge — so it overshoots to the end first and comes back up. Posted,
    /// because the rows don't exist to scroll to until the new ItemsSource has been laid out.
    /// </summary>
    private void ScrollToResume(List<ViewRow> rows)
    {
        int target = Math.Max(0, ViewRow.ResumeIndex(rows) - 2);
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(RowsList.ItemsSource, rows)) return; // filtered or switched since
            RowsList.ScrollIntoView(rows.Count - 1);
            RowsList.ScrollIntoView(target);
        }, DispatcherPriority.Background);
    }

    private void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessageText.IsVisible = true;
        RowsList.ItemsSource = null;
        CountText.Text = "";
    }

    private void SetButtonsEnabled(bool on)
    {
        MasterButton.IsEnabled = on;
        MustHearButton.IsEnabled = on;
        ReplacementsButton.IsEnabled = on;
        SearchBox.IsEnabled = on;
    }

    private async void OnReload(object? sender, RoutedEventArgs e)
    {
        _jumpToResume = true;
        await LoadAsync(force: true);
    }

    /// <summary>
    /// Opens the rater on this one album, whatever it's rated now — the way an existing rating gets
    /// changed. Modal, so the window can fold the result back into the row it came from when it
    /// closes rather than making the whole list reload for one cell.
    /// </summary>
    private async void OnRate(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ViewRow row } || !row.CanRate) return;

        var rater = new RatePage(RatingMode.Revisit, row.SheetRow);
        await new PageWindow("Change a rating", rater).ShowDialog((Window)TopLevel.GetTopLevel(this)!);

        if (rater.RatingsApplied.Count == 0) return;

        // The rater can move on down the queue, so more than the one row may have changed.
        if (_cache.TryGetValue(Which.Master, out var master))
        {
            foreach (var (sheetRow, rating) in rater.RatingsApplied)
            {
                var changed = master.FirstOrDefault(r => r.SheetRow == sheetRow);
                if (changed is not null) changed.Rating = rating;
            }
        }

        // A ⭐ given or taken away rewrites Must Hear, so the copy cached here is now a guess.
        // Drop it and let the tab reload from the sheet next time it's opened.
        _cache.Remove(Which.MustHear);

        ApplyFilter();
    }

    /// <summary>
    /// Queues a row into its own working playlist — the 1001 and Must Hear feed Playlist 1, the
    /// replacements list feeds Playlist 2, which is the one recommendations belong on.
    /// </summary>
    private void OnAddToPlaylist(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ViewRow row }) return;
        var playlist = row.Playlist == 2 ? _playlist2 : _playlist1;
        row.MarkQueued(playlist.Add(row.Title, row.Artist, row.Year));
    }
}
