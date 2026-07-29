using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>
/// Mobile counterpart to the desktop rating window: works through albums that still need a rating,
/// one at a time, writing each choice straight back to the master Google Sheet. Uses the REST Sheets
/// client (service account) rather than the desktop's OAuth writer, which needs a browser login that
/// doesn't work on iOS — see <see cref="RestSheetsClient"/>. Only the "not yet listened" queue is
/// offered on mobile; the desktop window's backfill/shuffle options aren't exposed here yet.
/// </summary>
public partial class RateView : UserControl
{
    private RatingSession? _session;
    private bool _busy;

    public RateView()
    {
        InitializeComponent();
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

        _session.Rebuild(RatingMode.NextUp, shuffle: false);
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
        _session.Skip();
        StatusText.Text = "Skipped — left unrated.";
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

        RemainingText.Text = $"{_session.Remaining} left to rate";
        BackButton.IsEnabled = _session.CanGoBack;

        var album = _session.Current;
        if (album is null)
        {
            ShowMessage("🎉 Nothing left in the queue — every album on the list has a mark.");
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
    }

    private void SetRatingButtonsEnabled(bool on)
    {
        foreach (var child in RatingRow.Children)
            if (child is Button b) b.IsEnabled = on;
    }
}
