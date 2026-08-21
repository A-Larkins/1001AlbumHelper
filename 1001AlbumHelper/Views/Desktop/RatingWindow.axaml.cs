using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>
/// Walks through albums a queue at a time, writing each choice straight back to the master Google
/// Sheet. Opens on whichever queue the caller asked for — and optionally straight onto one album,
/// which is how the list viewer sends a row here to have its rating changed.
/// </summary>
public partial class RatingWindow : Window
{
    private readonly RatingMode _initialMode;
    private readonly int? _focusSheetRow;
    private RatingSession? _session;
    private string? _ratingFilter;
    private bool _busy;

    /// <summary>
    /// What this window changed, keyed by sheet row — so a caller showing those albums can update
    /// its own rows when the window closes instead of re-reading the whole sheet.
    /// </summary>
    private readonly Dictionary<int, string> _applied = new();

    public IReadOnlyDictionary<int, string> RatingsApplied => _applied;

    /// <summary>Cancels the in-flight cover-art lookup when the card moves on to another album.</summary>
    private CancellationTokenSource? _artCts;

    public RatingWindow() : this(RatingMode.NextUp) { }

    /// <param name="focusSheetRow">
    /// A master-list row to open on, whatever its rating. Null starts at the head of the queue.
    /// </param>
    public RatingWindow(RatingMode initialMode, int? focusSheetRow = null)
    {
        InitializeComponent();
        _initialMode = initialMode;
        _focusSheetRow = focusSheetRow;
        SetMode(initialMode, rebuild: false);
        Opened += async (_, _) => await LoadAsync();
        KeyDown += OnKeyDown;
    }

    private async Task LoadAsync()
    {
        ShowMessage("Connecting to Google Sheets…");
        SetControlsEnabled(false);

        RatingSession? session;
        try
        {
            session = await Operations.OpenRatingSessionAsync();
        }
        catch (Exception ex)
        {
            ShowMessage($"Couldn't open the album list.\n\n{ex.Message}");
            return;
        }

        if (session is null)
        {
            ShowMessage("Couldn't open the album list — see the activity log in the main window for details.");
            return;
        }

        _session = session;
        _session.Rebuild(_initialMode, ShuffleBox.IsChecked == true, _ratingFilter);

        if (_focusSheetRow is { } row && !_session.FocusOn(row))
            StatusText.Text = $"Couldn't find row {row} on the list — showing the queue instead.";

        SetControlsEnabled(true);
        Render();
    }

    // ---------- Queue selection ----------
    private void OnPickNextUp(object? sender, RoutedEventArgs e) => SetMode(RatingMode.NextUp, rebuild: true);
    private void OnPickBackfill(object? sender, RoutedEventArgs e) => SetMode(RatingMode.Backfill, rebuild: true);
    private void OnPickRevisit(object? sender, RoutedEventArgs e) => SetMode(RatingMode.Revisit, rebuild: true);

    /// <summary>Narrows Revisit to one rating — the "All" button carries an empty tag.</summary>
    private void OnPickFilter(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;

        _ratingFilter = string.IsNullOrEmpty(tag) ? null : tag;
        foreach (var child in FilterPanel.Children)
            if (child is Button b) b.Classes.Set("on", (b.Tag as string ?? "") == (_ratingFilter ?? ""));

        if (_session is null) return;
        _session.Rebuild(RatingMode.Revisit, ShuffleBox.IsChecked == true, _ratingFilter);
        StatusText.Text = "";
        Render();
    }

    private void OnShuffleChanged(object? sender, RoutedEventArgs e)
    {
        // Fires while the window is still being constructed, before the session exists.
        if (_session is null) return;
        _session.Rebuild(_session.Mode, ShuffleBox.IsChecked == true, _ratingFilter);
        Render();
    }

    private void SetMode(RatingMode mode, bool rebuild)
    {
        NextUpButton.Classes.Set("on", mode == RatingMode.NextUp);
        BackfillButton.Classes.Set("on", mode == RatingMode.Backfill);
        RevisitButton.Classes.Set("on", mode == RatingMode.Revisit);
        FilterPanel.IsVisible = mode == RatingMode.Revisit;

        if (!rebuild || _session is null) return;
        _session.Rebuild(mode, ShuffleBox.IsChecked == true, _ratingFilter);
        StatusText.Text = "";
        Render();
    }

    // ---------- Rating ----------
    private async void OnRate(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string rating }) return;
        await RateAsync(rating);
    }

    private async Task RateAsync(string rating)
    {
        if (_busy || _session?.Current is null) return;

        _busy = true;
        SetControlsEnabled(false);
        StatusText.Text = $"Saving {rating}…";

        try
        {
            var result = await _session.RateCurrentAsync(rating);
            _applied[result.Album.SheetRow] = rating;
            StatusText.Text = $"✓ {rating} saved for “{result.Album.Title}” → {result.Cell}"
                            + (result.MustHearNote is null ? "" : $"  ·  {result.MustHearNote}");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"✗ {ex.Message}";
        }
        finally
        {
            _busy = false;
            SetControlsEnabled(true);
            Render();
        }
    }

    private void OnSkip(object? sender, RoutedEventArgs e)
    {
        if (_busy || _session is null) return;
        _session.Skip();
        StatusText.Text = _session.Mode == RatingMode.Revisit
            ? "Skipped — rating left as it was."
            : "Skipped — left unrated.";
        Render();
    }

    private void OnBack(object? sender, RoutedEventArgs e)
    {
        if (_busy || _session is null || !_session.CanGoBack) return;
        _session.Back();
        StatusText.Text = "";
        Render();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_busy || _session?.Current is null) return;

        string? rating = e.Key switch
        {
            Key.D1 or Key.NumPad1 => RatingSession.Starred,
            Key.D2 or Key.NumPad2 => RatingSession.Liked,
            Key.D3 or Key.NumPad3 => RatingSession.Disliked,
            Key.D4 or Key.NumPad4 => RatingSession.Trash,
            _ => null,
        };

        if (rating is not null) { e.Handled = true; await RateAsync(rating); }
        else if (e.Key == Key.S) { e.Handled = true; OnSkip(this, new RoutedEventArgs()); }
        else if (e.Key == Key.B) { e.Handled = true; OnBack(this, new RoutedEventArgs()); }
    }

    // ---------- Rendering ----------
    private void Render()
    {
        if (_session is null) return;

        string queueName = _session.Mode switch
        {
            RatingMode.NextUp => "not yet listened",
            RatingMode.Backfill => "listened, unrated",
            _ => _session.RatingFilter is { } only ? $"rated {only}" : "already rated",
        };
        RemainingText.Text = $"{_session.Remaining} left ({queueName})";
        BackButton.IsEnabled = _session.CanGoBack;

        var album = _session.Current;
        if (album is null)
        {
            ShowMessage(_session.Mode switch
            {
                RatingMode.NextUp => "🎉 Nothing left in the queue — every album on the list has a mark.",
                RatingMode.Backfill => "🎉 Nothing left to backfill — every ✓ album has a real rating.",
                _ => _session.RatingFilter is { } only
                    ? $"Nothing on the list is rated {only}."
                    : "Nothing on the list is rated yet — there's nothing to revisit.",
            });
            SetRatingButtonsEnabled(false);
            SkipButton.IsEnabled = false;
            return;
        }

        MessageText.IsVisible = false;
        AlbumPanel.IsVisible = true;
        SetRatingButtonsEnabled(true);

        PositionText.Text = $"#{album.Number} of {_session.TotalAlbums}  ·  row {album.SheetRow}";

        // Rating an album that already has one is a *change*, so say what's being changed from.
        CurrentRatingText.IsVisible = RatingSession.IsRated(album.Rating);
        CurrentRatingText.Text = $"currently rated {album.Rating} — pick again to change it";
        TitleText.Text = album.Title;
        ArtistText.Text = album.Artist;
        YearText.Text = album.Year;
        _ = ShowArtAsync(album);
    }

    /// <summary>
    /// Fills in the cover art for <paramref name="album"/> once the lookup returns, and warms the
    /// next album's art so it's already there when this one is rated. Runs alongside the rest of
    /// the card rather than holding it up — art is decoration, not information.
    /// </summary>
    private async Task ShowArtAsync(AlbumEntry album)
    {
        _artCts?.Cancel();
        _artCts = new CancellationTokenSource();
        var token = _artCts.Token;

        ArtImage.Source = null;
        ArtImage.IsVisible = false;
        ArtPlaceholder.IsVisible = true;

        var art = await AlbumArtwork.LoadAsync(album.Artist, album.Title, token);

        // The queue may have moved on while this was in flight — a cancelled lookup returns null,
        // but a finished one still has to prove it's for the album currently on screen.
        if (token.IsCancellationRequested || _session?.Current != album) return;

        if (art is not null)
        {
            ArtImage.Source = art;
            ArtImage.IsVisible = true;
            ArtPlaceholder.IsVisible = false;
        }

        if (_session?.Next is { } next) AlbumArtwork.Prefetch(next.Artist, next.Title);
    }

    private void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessageText.IsVisible = true;
        AlbumPanel.IsVisible = false;
    }

    private void SetControlsEnabled(bool on)
    {
        SetRatingButtonsEnabled(on && _session?.Current is not null);
        SkipButton.IsEnabled = on && _session?.Current is not null;
        BackButton.IsEnabled = on && (_session?.CanGoBack ?? false);
        NextUpButton.IsEnabled = on;
        BackfillButton.IsEnabled = on;
        RevisitButton.IsEnabled = on;
        foreach (var child in FilterPanel.Children)
            if (child is Button b) b.IsEnabled = on;
        ShuffleBox.IsEnabled = on;
    }

    private void SetRatingButtonsEnabled(bool on)
    {
        foreach (var child in RatingRow.Children)
            if (child is Button b) b.IsEnabled = on;
    }
}
