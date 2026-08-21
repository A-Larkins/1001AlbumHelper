using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>
/// Mobile counterpart to the desktop rating window: works through albums that still need a rating,
/// one at a time, writing each choice straight back to the master Google Sheet. Uses the REST Sheets
/// client (service account) rather than the desktop's OAuth writer, which needs a browser login that
/// doesn't work on iOS — see <see cref="RestSheetsClient"/>. It offers the same three queues as the
/// desktop window (shuffle is still Mac-only), plus a find box that jumps straight to any album by
/// name — the quickest way to re-rate one particular album on a phone.
/// </summary>
public partial class RateView : UserControl
{
    private RatingSession? _session;
    private RatingMode _mode = RatingMode.NextUp;
    private string? _ratingFilter;
    private bool _busy;

    /// <summary>Cancels the in-flight cover-art lookup when the card moves on to another album.</summary>
    private CancellationTokenSource? _artCts;

    public RateView()
    {
        InitializeComponent();
        FindBox.TextChanged += (_, _) => ShowMatches();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        ShowMessage("Connecting to Google Sheets…");
        SetControlsEnabled(false);

        var config = EmbeddedConfig.Load("appsettings.json");
        string? spreadsheetId = config["GoogleSheets:SpreadsheetId"];
        string albumsTab = config["GoogleSheets:AlbumsTab"] ?? "1001 albums";
        string starredTab = config["GoogleSheets:StarredTab"] ?? "Must Hear";
        string keyFile = config["GoogleSheets:ServiceAccountKeyFile"] ?? "service-account.json";
        string? keyJson = EmbeddedConfig.ReadFileOrEmbedded(keyFile);

        if (string.IsNullOrWhiteSpace(keyJson) || string.IsNullOrWhiteSpace(spreadsheetId))
        {
            ShowMessage("Rating needs Google Sheets sync, which isn't set up on this device.");
            return;
        }

        try
        {
            ISheetsClient client = new RestSheetsClient(keyJson, spreadsheetId);
            _session = await RatingSession.LoadAsync(client, albumsTab, starredTab);
        }
        catch (Exception ex)
        {
            ShowMessage($"Couldn't open the album list.\n\n{ex.Message}");
            return;
        }

        _session.Rebuild(_mode, shuffle: false, _ratingFilter);
        SetControlsEnabled(true);
        Render();
    }

    // ---------- Which queue ----------

    /// <summary>Switches queues. The filter strip only means anything for Revisit, so it follows.</summary>
    private void OnPickMode(object? sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { Tag: string tag }
            || !Enum.TryParse(tag, out RatingMode mode)) return;

        _mode = mode;
        NextUpButton.Classes.Set("on", mode == RatingMode.NextUp);
        BackfillButton.Classes.Set("on", mode == RatingMode.Backfill);
        RevisitButton.Classes.Set("on", mode == RatingMode.Revisit);
        FilterTrack.IsVisible = mode == RatingMode.Revisit;

        if (_session is null) return;
        _session.Rebuild(_mode, shuffle: false, _ratingFilter);
        StatusText.Text = "";
        Render();
    }

    /// <summary>Narrows Revisit to one rating — the "All" button carries an empty tag.</summary>
    private void OnPickFilter(object? sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { Tag: string tag }) return;

        _ratingFilter = string.IsNullOrEmpty(tag) ? null : tag;
        foreach (var child in FilterRow.Children)
            if (child is Button b) b.Classes.Set("on", (b.Tag as string ?? "") == (_ratingFilter ?? ""));

        if (_session is null) return;
        _session.Rebuild(RatingMode.Revisit, shuffle: false, _ratingFilter);
        StatusText.Text = "";
        Render();
    }

    // ---------- Finding an album ----------

    /// <summary>Lists what the find box matches, hiding itself again when the box is emptied.</summary>
    private void ShowMatches()
    {
        string query = FindBox.Text?.Trim() ?? "";
        var matches = _session?.Search(query) ?? Array.Empty<AlbumEntry>();

        FindResults.ItemsSource = matches;
        FindResults.IsVisible = matches.Count > 0;
    }

    /// <summary>
    /// Jumps the card to the tapped album, whatever it's rated now, and clears the search so the
    /// list gets out of the way of the rating buttons.
    /// </summary>
    private void OnPickFound(object? sender, SelectionChangedEventArgs e)
    {
        if (_busy || _session is null || FindResults.SelectedItem is not AlbumEntry album) return;

        FindResults.SelectedItem = null;
        FindBox.Text = "";
        ShowMatches();

        _session.FocusOn(album.SheetRow);
        StatusText.Text = RatingSession.IsRated(album.Rating)
            ? $"Jumped to “{album.Title}” — rated {album.Rating}."
            : $"Jumped to “{album.Title}”.";
        SetControlsEnabled(true);
        Render();
    }

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
            StatusText.Text = "✓ Saved" + (result.MustHearNote is null ? "" : $" · {result.MustHearNote}");
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

        // Read the album being skipped before stepping past it — afterwards Current is the next one.
        bool wasRated = RatingSession.IsRated(_session.Current?.Rating ?? "");
        _session.Skip();
        StatusText.Text = wasRated
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

    private void Render()
    {
        if (_session is null) return;

        RemainingText.Text = _session.Mode switch
        {
            RatingMode.NextUp => $"{_session.Remaining} left to rate",
            RatingMode.Backfill => $"{_session.Remaining} left to backfill",
            _ => _session.RatingFilter is { } only
                ? $"{_session.Remaining} rated {only}"
                : $"{_session.Remaining} already rated",
        };
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

        PositionText.Text = $"#{album.Number} of {_session.TotalAlbums}";
        TitleText.Text = album.Title;
        ArtistText.Text = album.Artist;
        YearText.Text = album.Year;

        // Rating an album that already has one is a *change*, so say what's being changed from.
        CurrentRatingText.IsVisible = RatingSession.IsRated(album.Rating);
        CurrentRatingText.Text = $"currently rated {album.Rating} — pick again to change it";

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
        FindBox.IsEnabled = on && _session is not null;
        foreach (var row in new[] { ModeRow, FilterRow })
            foreach (var child in row.Children)
                if (child is Button b) b.IsEnabled = on && _session is not null;
    }

    private void SetRatingButtonsEnabled(bool on)
    {
        foreach (var child in RatingRow.Children)
            if (child is Button b) b.IsEnabled = on;
    }
}
