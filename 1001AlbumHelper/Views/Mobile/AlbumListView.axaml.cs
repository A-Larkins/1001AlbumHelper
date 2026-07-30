using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace _1001AlbumHelper;

/// <summary>
/// Mobile browser over all three lists — the 1001 (offline snapshot), Must Hear, and the final
/// numbered Replacements list — with a search box and a per-row "add to Playlist 1". The 1001 is
/// instant and always available; Must Hear/Replacements are read live from Google Sheets via the
/// REST client (mobile's OAuth-free path — see RestSheetsClient) the first time each is opened, then
/// cached for the rest of the session.
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
    private ViewRowSortColumn? _sortColumn;
    private bool _sortDescending;

    public AlbumListView()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        Loaded += (_, _) => Load1001();
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
    }

    private void ShowList(string list)
    {
        _active = list;
        Tab1001Button.Classes.Set("on", list == Tab1001);
        TabMustHearButton.Classes.Set("on", list == TabMustHear);
        TabReplacementsButton.Classes.Set("on", list == TabReplacements);

        if (_cache.TryGetValue(list, out var cached))
        {
            _all = cached;
            SyncText.Text = list == Tab1001 ? "" : "✓ Live from Google Sheets";
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

        Dispatcher.UIThread.Post(() =>
        {
            Rows.SelectedItem = target;
            Rows.ScrollIntoView(target);
        }, DispatcherPriority.Loaded);
    }

    private async System.Threading.Tasks.Task LoadLiveListAsync(string list)
    {
        SyncText.Text = $"Loading “{list}” from Google Sheets…";

        var config = EmbeddedConfig.Load("appsettings.json");
        string? spreadsheetId = config["GoogleSheets:SpreadsheetId"];
        string tab = list == TabMustHear
            ? config["GoogleSheets:StarredTab"] ?? "Must Hear"
            : config["GoogleSheets:ReplacementsTab"] ?? "Replacements";
        string keyFile = config["GoogleSheets:ServiceAccountKeyFile"] ?? "service-account.json";
        string? keyJson = EmbeddedConfig.ReadFileOrEmbedded(keyFile);

        if (string.IsNullOrWhiteSpace(keyJson) || string.IsNullOrWhiteSpace(spreadsheetId))
        {
            SyncText.Text = "⚠ Sheets sync isn't set up on this device — nothing to browse here.";
            return;
        }

        try
        {
            ISheetsClient client = new RestSheetsClient(keyJson, spreadsheetId);
            var contents = await NumberedList.ReadAsync(client, tab);
            var rows = contents.Rows
                .Select(r => new ViewRow(r.Number, "", r.Title, r.Artist, r.Year))
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

    private void OnAddToPlaylist1(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not ViewRow row) return;
        bool added = _playlist1.Add(row.Title, row.Artist, row.Year);
        button.Content = added ? "✓ P1" : "· P1"; // ✓ = just added, · = was already on it
        button.IsEnabled = false;
    }
}
