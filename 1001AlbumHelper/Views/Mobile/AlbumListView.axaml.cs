using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>Mobile browser over the 1001 list, with a search box and a per-row "add to Playlist 1".</summary>
public partial class AlbumListView : UserControl
{
    private List<ViewRow> _all = new();
    private readonly PlaylistStore _playlist1 = PlaylistStore.Open(1);

    public AlbumListView()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        try
        {
            _all = MobileData.Load1001();
        }
        catch (Exception ex)
        {
            CountText.Text = $"Couldn't load the list: {ex.Message}";
            return;
        }
        ApplyFilter();
    }

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

        Rows.ItemsSource = shown;
        CountText.Text = shown.Count == _all.Count
            ? $"{_all.Count} albums"
            : $"{shown.Count} of {_all.Count} albums";
    }

    private void OnAddToPlaylist1(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not ViewRow row) return;
        bool added = _playlist1.Add(row.Title, row.Artist, row.Year);
        button.Content = added ? "✓ P1" : "· P1"; // ✓ = just added, · = was already on it
        button.IsEnabled = false;
    }
}
