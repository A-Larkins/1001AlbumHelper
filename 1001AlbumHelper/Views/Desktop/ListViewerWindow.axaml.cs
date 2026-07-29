using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>Read-only browser over the three lists, with a search box.</summary>
public partial class ListViewerWindow : Window
{
    private enum Which { Master, MustHear, Replacements }

    private readonly Dictionary<Which, List<ViewRow>> _cache = new();
    private Which _current = Which.Master;

    public ListViewerWindow()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        Opened += async (_, _) => await LoadAsync();
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
                .Select(a => new ViewRow(a.Number, a.Rating, a.Title, a.Artist, a.Year)).ToList();
            _cache[Which.MustHear] = loaded.MustHear
                .Select(r => new ViewRow(r.Number, "", r.Title, r.Artist, r.Year)).ToList();
            _cache[Which.Replacements] = loaded.Replacements
                .Select(r => new ViewRow(r.Number, "", r.Title, r.Artist, r.Year)).ToList();

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

    private void Show(Which which)
    {
        _current = which;
        MasterButton.Classes.Set("on", which == Which.Master);
        MustHearButton.Classes.Set("on", which == Which.MustHear);
        ReplacementsButton.Classes.Set("on", which == Which.Replacements);

        // Only the master list carries ratings.
        RatingHeader.Text = which == Which.Master ? "RATING" : "";

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

    private async void OnReload(object? sender, RoutedEventArgs e) => await LoadAsync(force: true);
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
