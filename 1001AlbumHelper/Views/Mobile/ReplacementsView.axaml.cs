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
    private bool _isLive;   // true once the list came from Google Sheets (not the baked-in snapshot)
    private readonly PlaylistStore _playlist2 = PlaylistStore.Open(2);
    private readonly CandidateRepository _repo = CandidateRepository.Create();

    public ReplacementsView()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        Loaded += (_, _) => Load();
    }

    private async void Load()
    {
        // Instant: the snapshot baked into the app.
        try { _all = MobileData.LoadCandidates(); ApplyFilter(); }
        catch (Exception ex) { CountText.Text = $"Couldn't load candidates: {ex.Message}"; }

        // Then: the live shared list from Google Sheets, if sync is configured on this device.
        if (_repo.SyncEnabled)
        {
            try
            {
                var live = await _repo.PullAsync();
                if (live is not null)
                {
                    _all = live;
                    _isLive = true;
                    ApplyFilter();
                }
            }
            catch (Exception ex)
            {
                CountText.Text += $"  ·  live sync failed: {ex.Message}";
            }
        }
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
        string count = shown.Count == _all.Count
            ? $"{_all.Count} candidates"
            : $"{shown.Count} of {_all.Count} candidates";
        CountText.Text = _isLive ? $"{count} · live ✓" : count;
    }

    private void OnAddToPlaylist2(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not CandidateAlbum album) return;
        bool added = _playlist2.Add(album.Title, album.Artist, album.Year);
        button.Content = added ? "✓ P2" : "· P2";
        button.IsEnabled = false;
    }
}
