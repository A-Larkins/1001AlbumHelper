using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>
/// Mobile browser over the potential-replacements shortlist: search, add new candidates (with
/// Discogs year lookup), edit a year in place, and add a row to Playlist 2. Adding and editing both
/// require live Sheets sync — there's no writable local cache on the phone, so an edit that couldn't
/// reach Sheets would otherwise vanish the moment the view reloads.
/// </summary>
public partial class ReplacementsView : UserControl
{
    private List<CandidateAlbum> _all = new();
    private bool _isLive;   // true once the list came from Google Sheets (not the baked-in snapshot)
    private readonly PlaylistStore _playlist2 = PlaylistStore.Open(2);
    private readonly CandidateRepository _repo = CandidateRepository.Create();

    // Null when no Discogs token is configured: adding still works, just without lookup.
    private readonly DiscogsApiClient? _discogs = DiscogsApiClient.TryCreate();

    public ReplacementsView()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        Loaded += (_, _) => Load();

        LookupHint.Text = AlbumLookup.Attach(
            _discogs, TitleBox, ArtistBox, YearBox,
            pick => { LookupHint.Text = AlbumLookup.Picked(pick); AddStatusText.Text = ""; });
    }

    private async void Load()
    {
        // Instant: the snapshot baked into the app.
        try { SetAll(MobileData.LoadCandidates()); }
        catch (Exception ex) { CountText.Text = $"Couldn't load candidates: {ex.Message}"; }

        // Then: the live shared list from Google Sheets. Show exactly what happened either way.
        if (!_repo.SyncEnabled)
        {
            SyncText.Text = $"⚠ Sheets sync {_repo.Status} — browsing only, nothing here can be saved.";
            AddToggle.IsEnabled = false;
            return;
        }

        SyncText.Text = "Syncing with Google Sheets…";
        try
        {
            var live = await _repo.PullAsync();
            if (live is not null)
            {
                _isLive = true;
                SetAll(live);
                // The count line below already says "· live ✓" once synced — no need to repeat it here.
                SyncText.Text = "";
            }
        }
        catch (Exception ex)
        {
            SyncText.Text = $"⚠ Sync failed — {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>Swaps in a fresh candidate list, watching each album's Year so an in-place edit pushes to Sheets.</summary>
    private void SetAll(List<CandidateAlbum> albums)
    {
        foreach (var album in _all) album.PropertyChanged -= OnAlbumChanged;
        _all = albums;
        foreach (var album in _all) album.PropertyChanged += OnAlbumChanged;
        ApplyFilter();
    }

    private void OnAlbumChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CandidateAlbum.Year)) PushToSheets();
    }

    /// <summary>Pushes the shortlist to Google Sheets in the background (no-op when sync is off).</summary>
    private async void PushToSheets()
    {
        if (!_repo.SyncEnabled) return;
        try { await _repo.PushAsync(_all); }
        catch (Exception ex) { SyncText.Text = $"⚠ Saved here, but Sheets sync failed: {ex.Message}"; }
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

    // ---------- Add to shortlist ----------

    private void OnToggleAdd(object? sender, RoutedEventArgs e)
    {
        bool showing = !AddPanel.IsVisible;
        AddPanel.IsVisible = showing;
        AddToggle.Content = showing ? "▲ Add album" : "＋ Add album";
        if (showing) TitleBox.Focus();
    }

    private void OnAddCandidate(object? sender, RoutedEventArgs e)
    {
        string title = TitleBox.Text?.Trim() ?? "";
        string artist = ArtistBox.Text?.Trim() ?? "";
        string year = YearBox.Text?.Trim() ?? "";

        if (title.Length == 0) { AddFail("Enter the album title."); return; }
        if (artist.Length == 0) { AddFail("Enter the artist."); return; }
        if (year.Length > 0 && (!int.TryParse(year, out int y) || y < 1900 || y > DateTime.Now.Year + 1))
        {
            AddFail("That year doesn't look right — four digits, or leave it blank.");
            return;
        }

        var (outcome, match) = ReplacementCandidates.Classify(_all, title, artist);
        CandidateAlbum album;
        switch (outcome)
        {
            case CandidateAddOutcome.AlreadyPending:
                AddFail($"“{match!.Title}” by {match.Artist} is already on the shortlist, waiting to be decided on.");
                return;
            case CandidateAddOutcome.AlreadyKept:
                AddFail($"“{match!.Title}” has already been kept — it's on your replacements list.");
                return;
            case CandidateAddOutcome.Reopen:
                album = match!;
                album.Status = CandidateStatus.Pending;
                album.Note = "";
                break;
            default:
                album = new CandidateAlbum { Title = title, Artist = artist, Year = year, Status = CandidateStatus.Pending };
                _all.Add(album);
                album.PropertyChanged += OnAlbumChanged;
                break;
        }

        PushToSheets();
        TitleBox.Text = "";
        ArtistBox.Text = "";
        YearBox.Text = "";
        SearchBox.Text = ""; // clear the filter, or the new/reopened row may be hidden by it
        ApplyFilter();

        AddStatusText.Text = outcome == CandidateAddOutcome.Reopen
            ? $"“{album.Title}” is back on the shortlist."
            : $"Added “{album.Title}” to the shortlist"
              + (album.Year.Length > 0 ? $" ({album.Year})." : " — enter a year when you know it.");
    }

    private void AddFail(string message) => AddStatusText.Text = $"✗ {message}";
}
