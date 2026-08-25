using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace _1001AlbumHelper;

/// <summary>
/// Mobile browser over all three lists — the 1001, Must Hear, and the final numbered Replacements
/// list — with a search box and a per-row "add to playlist" that follows the list you're in: the
/// 1001 and Must Hear go to Playlist 1, Replacements to Playlist 2. Must Hear/Replacements are read
/// live from Google Sheets via the REST client (mobile's OAuth-free path — see RestSheetsClient) the
/// first time each is opened, then cached for the rest of the session. The 1001 opens instantly from
/// the offline snapshot and is then replaced by the live list (see <see cref="Refresh1001Async"/>),
/// so it works with no signal but doesn't strand you on the ratings the app was built with.
/// </summary>
public partial class AlbumListView : UserControl
{
    private const string Tab1001 = "1001";
    private const string TabMustHear = "Must Hear";
    private const string TabReplacements = "Replacements";

    private readonly Dictionary<string, List<ViewRow>> _cache = new();
    private string _active = Tab1001;
    private List<ViewRow> _all = new();
    private readonly PlaylistStore _playlist1 = PlaylistStore.Open(1);
    private readonly PlaylistStore _playlist2 = PlaylistStore.Open(2);
    private ViewRowSortColumn? _sortColumn;
    private bool _sortDescending;

    /// <summary>
    /// The row the 1001 tab parked itself on — where you actually are in the list (see
    /// <see cref="ScrollToProgress"/>). Kept so that scrolling off to look something up somewhere
    /// else in the 1001 doesn't cost you your place: while it's off screen the "back to" chip
    /// offers to put you back on it. Null on the other two tabs, which have no such spot.
    /// </summary>
    private ViewRow? _anchor;

    /// <summary>
    /// Where the anchor sits in the rows currently on screen, or -1 when it isn't among them.
    /// Cached rather than looked up per scroll event: the 1001 is a thousand rows and the check runs
    /// on every frame of a flick, which is not the place for a linear search. Only what's shown can
    /// move it, so <see cref="ApplyFilter"/> is the one place that recomputes it.
    /// </summary>
    private int _anchorIndex = -1;

    /// <summary>The list's own ScrollViewer, found once its template is up — see <see cref="AttachScroll"/>.</summary>
    private ScrollViewer? _scroller;

    /// <summary>True once the 1001 tab is showing the sheet's copy rather than the built-in one.</summary>
    private bool _live1001;

    public AlbumListView()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        Loaded += (_, _) => { AttachScroll(); Load1001(); };
        Rows.TemplateApplied += (_, _) => AttachScroll();
        UpdateSortIndicators();
    }

    private void Load1001()
    {
        try { _cache[Tab1001] = MobileData.Load1001(); }
        catch (Exception ex)
        {
            _cache[Tab1001] = new List<ViewRow>();
            CountText.Text = $"Couldn't load the list: {ex.Message}";
        }
        ShowList(Tab1001);

        // The snapshot is only as fresh as the last build, so bring it up to date behind the scenes.
        _ = Refresh1001Async();
    }

    /// <summary>
    /// Replaces the built-in 1001 snapshot with what the sheet says now. That snapshot is baked in
    /// at build time, so the ratings in it are the first thing to go stale — this catches them up,
    /// including ratings given on the Mac. Deliberately runs *after* the snapshot is already on
    /// screen: the tab still opens instantly, and still works with no signal.
    /// </summary>
    private async System.Threading.Tasks.Task Refresh1001Async()
    {
        var sheets = MobileSheets.Create();
        if (sheets.Client is null) return; // not set up here — the snapshot is all there is

        try
        {
            var live = await RatingSession.LoadAsync(sheets.Client, sheets.AlbumsTab, sheets.StarredTab);
            var rows = live.AllAlbums
                .Select(a => new ViewRow(a.Number, a.Rating, a.Title, a.Artist, a.Year,
                                         sheetRow: a.SheetRow))
                .ToList();

            _cache[Tab1001] = rows;
            _live1001 = true;
            if (_active != Tab1001) return;

            _all = rows;
            SyncText.Text = $"✓ Live from Google Sheets ({rows.Count})";
            ApplyFilter();
            ScrollToProgress();
        }
        catch (Exception ex)
        {
            // Not fatal: the snapshot on screen is still perfectly usable, just older.
            if (_active == Tab1001) SyncText.Text = $"⚠ Showing the built-in copy — {ex.Message}";
        }
    }

    private void ShowList(string list)
    {
        _active = list;
        Tab1001Button.Classes.Set("on", list == Tab1001);
        TabMustHearButton.Classes.Set("on", list == TabMustHear);
        TabReplacementsButton.Classes.Set("on", list == TabReplacements);

        if (_cache.TryGetValue(list, out var cached))
        {
            // Anything rated this run is newer than what any cached list holds.
            if (RatingChanges.Count > 0) RatingChanges.ApplyTo(cached);

            _all = cached;
            SyncText.Text = list == Tab1001 && !_live1001 ? "" : "✓ Live from Google Sheets";
            ApplyFilter();
            if (list == Tab1001) ScrollToProgress();
        }
        else
        {
            _all = new List<ViewRow>();
            ApplyFilter();
            _ = LoadLiveListAsync(list);
        }
    }

    /// <summary>
    /// Jumps the list to wherever you actually are — the first album (in list order) with neither a
    /// rating nor a "✓ listened" mark — so opening the 1001 tab shows your progress instead of always
    /// starting at #1. Only makes sense with the search box empty (a filtered view might not even
    /// contain that row).
    /// </summary>
    private void ScrollToProgress()
    {
        if (!string.IsNullOrWhiteSpace(SearchBox.Text)) return;

        var target = _all.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.Rating));
        if (target is null) return;

        _anchor = target;
        _anchorIndex = (Rows.ItemsSource as List<ViewRow>)?.IndexOf(target) ?? -1;

        Dispatcher.UIThread.Post(() =>
        {
            Rows.SelectedItem = target;
            Rows.ScrollIntoView(target);
            UpdateJumpBack();
        }, DispatcherPriority.Loaded);
    }

    // ---------- Keeping your place ----------

    /// <summary>
    /// Subscribes to the list's scrolling the first time its ScrollViewer exists. Called from both
    /// Loaded and TemplateApplied because which of the two comes first isn't ours to decide, and
    /// guarded so the second one is a no-op.
    /// </summary>
    private void AttachScroll()
    {
        if (_scroller is not null) return;
        _scroller = Rows.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_scroller is null) return;
        _scroller.ScrollChanged += (_, _) => UpdateJumpBack();
    }

    /// <summary>
    /// Shows the "back to" chip exactly while the anchor row is off screen, and names the album so
    /// it's obvious where it goes. Anything that makes the anchor unreachable — another tab, a
    /// search that filters it out, a sort that has moved it who knows where — hides the chip
    /// instead of offering a jump that would land somewhere unexpected.
    /// </summary>
    private void UpdateJumpBack()
    {
        if (_anchorIndex < 0 || _anchor is null || AnchorIsOnScreen(_anchorIndex))
        {
            JumpBackButton.IsVisible = false;
            return;
        }

        JumpBackButton.Content = $"↩ Back to #{_anchor.Number}  {_anchor.Title}";
        JumpBackButton.IsVisible = true;
    }

    /// <summary>
    /// Whether row <paramref name="index"/> is within the visible slice of the list. Measured off
    /// the container's position relative to the ScrollViewer rather than off "is it realized",
    /// because a short list isn't virtualized at all and would otherwise always read as on screen.
    /// </summary>
    private bool AnchorIsOnScreen(int index)
    {
        if (_scroller is null) return true; // no scrolling yet — nothing to have scrolled away from
        if (Rows.ContainerFromIndex(index) is not { } container) return false; // virtualized away
        if (container.TranslatePoint(new Point(0, 0), _scroller) is not { } top) return false;
        return top.Y + container.Bounds.Height > 0 && top.Y < _scroller.Bounds.Height;
    }

    /// <summary>Puts you back on the row you were working from.</summary>
    private void OnJumpBack(object? sender, RoutedEventArgs e)
    {
        if (_anchor is null) return;
        Rows.SelectedItem = _anchor;
        Rows.ScrollIntoView(_anchor);
        UpdateJumpBack();
    }

    private async System.Threading.Tasks.Task LoadLiveListAsync(string list)
    {
        SyncText.Text = $"Loading “{list}” from Google Sheets…";

        var sheets = MobileSheets.Create();
        if (sheets.Client is null)
        {
            SyncText.Text = $"⚠ Sheets sync {sheets.Status} — nothing to browse here.";
            return;
        }

        string tab = list == TabMustHear ? sheets.StarredTab : sheets.ReplacementsTab;

        try
        {
            var contents = await NumberedList.ReadAsync(sheets.Client, tab);
            var rows = contents.Rows
                .Select(r => new ViewRow(r.Number, "", r.Title, r.Artist, r.Year,
                                         playlist: list == TabReplacements ? 2 : 1))
                .ToList();

            _cache[list] = rows;
            if (_active == list)
            {
                _all = rows;
                SyncText.Text = $"✓ Live from Google Sheets ({rows.Count})";
                ApplyFilter();
            }
        }
        catch (Exception ex)
        {
            if (_active == list) SyncText.Text = $"⚠ Couldn't load “{list}” — {ex.Message}";
        }
    }

    private void OnPick1001(object? sender, RoutedEventArgs e) => ShowList(Tab1001);
    private void OnPickMustHear(object? sender, RoutedEventArgs e) => ShowList(TabMustHear);
    private void OnPickReplacements(object? sender, RoutedEventArgs e) => ShowList(TabReplacements);

    private void ApplyFilter()
    {
        string query = SearchBox.Text?.Trim() ?? "";
        List<ViewRow> shown;
        if (query.Length == 0)
        {
            shown = _all;
        }
        else
        {
            var terms = NumberedList.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            shown = _all.Where(r =>
            {
                string hay = r.Haystack.ToLowerInvariant();
                return terms.All(t => hay.Contains(t, StringComparison.Ordinal));
            }).ToList();
        }

        if (_sortColumn is { } column) shown = ViewRowSort.Sort(shown, column, _sortDescending);

        Rows.ItemsSource = shown;
        CountText.Text = shown.Count == _all.Count
            ? $"{_all.Count} albums"
            : $"{shown.Count} of {_all.Count} albums";

        // Anything that makes the anchor unreachable — another tab, a search that filters it out —
        // leaves it at -1, and the chip stays down rather than offering a jump to nowhere.
        _anchorIndex = _active == Tab1001 && _anchor is not null ? shown.IndexOf(_anchor) : -1;

        // The rows have moved under the chip; work out again whether it still has a place to offer.
        Dispatcher.UIThread.Post(UpdateJumpBack, DispatcherPriority.Loaded);
    }

    // ---------- Sorting ----------

    /// <summary>Tap a column to sort by it ascending; tap the active one again to flip direction.</summary>
    private void OnSortHeader(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse(tag, out ViewRowSortColumn column))
            return;

        if (_sortColumn == column) _sortDescending = !_sortDescending;
        else { _sortColumn = column; _sortDescending = false; }

        UpdateSortIndicators();
        ApplyFilter();
    }

    private void UpdateSortIndicators()
    {
        (ViewRowSortColumn Column, Button Button, string Label)[] buttons =
        {
            (ViewRowSortColumn.Number, SortNumberButton, "#"),
            (ViewRowSortColumn.Title, SortTitleButton, "Title"),
            (ViewRowSortColumn.Artist, SortArtistButton, "Artist"),
            (ViewRowSortColumn.Year, SortYearButton, "Year"),
            (ViewRowSortColumn.Rating, SortRatingButton, "Rating"),
        };

        foreach (var (column, button, label) in buttons)
        {
            bool active = _sortColumn == column;
            button.Classes.Set("on", active);
            button.Content = active ? $"{label} {(_sortDescending ? "▼" : "▲")}" : label;
        }
    }

    /// <summary>
    /// Queues a row into its own working playlist — the 1001 and Must Hear feed Playlist 1, the
    /// Replacements list feeds Playlist 2, which is the one recommendations belong on.
    /// </summary>
    private void OnAddToPlaylist(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ViewRow row }) return;
        var playlist = row.Playlist == 2 ? _playlist2 : _playlist1;
        row.MarkQueued(playlist.Add(row.Title, row.Artist, row.Year)); // ✓ = just added, · = was already on it
    }
}
