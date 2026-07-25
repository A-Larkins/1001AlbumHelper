using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>Mobile browser over the potential-replacements shortlist, with a per-row "add to Playlist 2".</summary>
public partial class ReplacementsView : UserControl
{
    private List<CandidateAlbum> _all = new();
    private readonly PlaylistStore _playlist2 = PlaylistStore.Open(2);

    public ReplacementsView()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        try
        {
            _all = MobileData.LoadCandidates();
        }
        catch (Exception ex)
        {
            CountText.Text = $"Couldn't load candidates: {ex.Message}";
            return;
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string query = SearchBox.Text?.Trim() ?? "";
        List<CandidateAlbum> shown;
        if (query.Length == 0)
        {
            shown = _all;
        }
        else
        {
            var terms = NumberedList.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            shown = _all.Where(a =>
            {
                string hay = $"{NumberedList.Normalize(a.Title)} {NumberedList.Normalize(a.Artist)}";
                return terms.All(t => hay.Contains(t, StringComparison.Ordinal));
            }).ToList();
        }

        Rows.ItemsSource = shown;
        CountText.Text = shown.Count == _all.Count
            ? $"{_all.Count} candidates"
            : $"{shown.Count} of {_all.Count} candidates";
    }

    private void OnAddToPlaylist2(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not CandidateAlbum album) return;
        bool added = _playlist2.Add(album.Title, album.Artist, album.Year);
        button.Content = added ? "✓ P2" : "· P2";
        button.IsEnabled = false;
    }
}
