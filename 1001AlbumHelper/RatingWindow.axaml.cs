using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>
/// Walks through albums that still need a rating, writing each choice straight back to the
/// master Google Sheet. Opens on whichever queue the caller asked for.
/// </summary>
public partial class RatingWindow : Window
{
    private readonly RatingMode _initialMode;
    private RatingSession? _session;
    private bool _busy;

    public RatingWindow() : this(RatingMode.NextUp) { }

    public RatingWindow(RatingMode initialMode)
    {
        InitializeComponent();
        _initialMode = initialMode;
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
        _session.Rebuild(_initialMode, ShuffleBox.IsChecked == true);
        SetControlsEnabled(true);
        Render();
    }

    // ---------- Queue selection ----------
    private void OnPickNextUp(object? sender, RoutedEventArgs e) => SetMode(RatingMode.NextUp, rebuild: true);
    private void OnPickBackfill(object? sender, RoutedEventArgs e) => SetMode(RatingMode.Backfill, rebuild: true);

    private void OnShuffleChanged(object? sender, RoutedEventArgs e)
    {
        // Fires while the window is still being constructed, before the session exists.
        if (_session is null) return;
        _session.Rebuild(_session.Mode, ShuffleBox.IsChecked == true);
        Render();
    }

    private void SetMode(RatingMode mode, bool rebuild)
    {
        NextUpButton.Classes.Set("on", mode == RatingMode.NextUp);
        BackfillButton.Classes.Set("on", mode == RatingMode.Backfill);

        if (!rebuild || _session is null) return;
        _session.Rebuild(mode, ShuffleBox.IsChecked == true);
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
        StatusText.Text = "Skipped — left unrated.";
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
    }

    // ---------- Rendering ----------
    private void Render()
    {
        if (_session is null) return;

        string queueName = _session.Mode == RatingMode.NextUp ? "not yet listened" : "listened, unrated";
        RemainingText.Text = $"{_session.Remaining} left ({queueName})";

        var album = _session.Current;
        if (album is null)
        {
            ShowMessage(_session.Mode == RatingMode.NextUp
                ? "🎉 Nothing left in the queue — every album on the list has a mark."
                : "🎉 Nothing left to backfill — every ✓ album has a real rating.");
            SetRatingButtonsEnabled(false);
            return;
        }

        MessageText.IsVisible = false;
        AlbumPanel.IsVisible = true;
        SetRatingButtonsEnabled(true);

        PositionText.Text = $"#{album.Number} of {_session.TotalAlbums}  ·  row {album.SheetRow}";
        TitleText.Text = album.Title;
        ArtistText.Text = album.Artist;
        YearText.Text = album.Year;
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
        NextUpButton.IsEnabled = on;
        BackfillButton.IsEnabled = on;
        ShuffleBox.IsEnabled = on;
    }

    private void SetRatingButtonsEnabled(bool on)
    {
        foreach (var child in RatingRow.Children)
            if (child is Button b) b.IsEnabled = on;
    }
}
