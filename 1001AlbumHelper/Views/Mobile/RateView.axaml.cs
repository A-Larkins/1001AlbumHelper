using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace _1001AlbumHelper;

/// <summary>
/// Mobile counterpart to the desktop rating window: works through albums that still need a rating,
/// one at a time, writing each choice straight back to the master Google Sheet. Uses the REST Sheets
/// client (service account) rather than the desktop's OAuth writer, which needs a browser login that
/// doesn't work on iOS — see <see cref="RestSheetsClient"/>. It offers the same three queues as the
/// desktop window (shuffle is still Mac-only), plus a search field over <see cref="RatingSession.Search"/>
/// that reaches any album on the list — the quickest way to re-rate one particular album on a phone.
/// </summary>
public partial class RateView : UserControl
{
    /// <summary>How the picker orders the list, which is also what its rail indexes.</summary>
    private enum FindSort { Title, Artist, Number }

    private RatingSession? _session;
    private RatingMode _mode = RatingMode.NextUp;
    private string? _ratingFilter;
    private bool _busy;

    /// <summary>Cancels the in-flight cover-art lookup when the card moves on to another album.</summary>
    private CancellationTokenSource? _artCts;

    private FindSort _findSort = FindSort.Title;

    /// <summary>What the picker is showing, in picker order — what the rail scrolls through.</summary>
    private List<AlbumEntry> _found = new();

    /// <summary>One rail stop: its label, and the first row in <see cref="_found"/> it jumps to.</summary>
    private List<(string Label, int Index)> _rail = new();

    public RateView()
    {
        InitializeComponent();
        FindBox.TextChanged += (_, _) => { OpenFind(); ShowMatches(); };
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

    /// <summary>
    /// Tapping the field shows the results under it. Opening on focus rather than on the first
    /// keystroke means the list is already there to scroll while the keyboard is still on its way
    /// up, and an empty field lists everything, so nothing has to be typed to get somewhere.
    /// </summary>
    private void OnFindFocus(object? sender, GotFocusEventArgs e) => OpenFind();

    /// <summary>
    /// The A–Z button: the same results, without asking for the keyboard. This is the one that
    /// makes the index rail worth having — raise the keyboard and it has half the height to work in.
    /// </summary>
    private void OnBrowse(object? sender, RoutedEventArgs e) => OpenFind();

    private void OpenFind()
    {
        if (_session is null || FindSheet.IsVisible) return;

        ShowMatches();
        FindSheet.IsVisible = true;
        CancelFindButton.IsVisible = true;
    }

    private void OnCloseFind(object? sender, RoutedEventArgs e) => CloseFind();

    /// <summary>
    /// Puts the results away and drops focus with them — without clearing focus the field keeps it,
    /// and iOS keeps the keyboard up over a screen that no longer has anything to type into.
    /// </summary>
    private void CloseFind()
    {
        FindSheet.IsVisible = false;
        CancelFindButton.IsVisible = false;
        RailBubble.IsVisible = false;
        if (!string.IsNullOrEmpty(FindBox.Text)) FindBox.Text = "";
        TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
    }

    /// <summary>Re-orders the picker. The rail is rebuilt with it, since it indexes this order.</summary>
    private void OnPickFindSort(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse(tag, out FindSort sort)) return;

        _findSort = sort;
        foreach (var child in FindSortRow.Children)
            if (child is Button b) b.Classes.Set("on", (b.Tag as string) == tag);

        ShowMatches();
    }

    /// <summary>
    /// Fills the results: everything when the field is empty (so they read as a browsable list
    /// rather than a blank slate), the matches once it isn't, in the chosen order either way.
    /// </summary>
    private void ShowMatches()
    {
        string query = FindBox.Text?.Trim() ?? "";

        IEnumerable<AlbumEntry> matches = string.IsNullOrEmpty(query)
            ? _session?.AllAlbums ?? Array.Empty<AlbumEntry>()
            : _session?.Search(query, limit: int.MaxValue) ?? Array.Empty<AlbumEntry>();

        _found = _findSort == FindSort.Number
            ? matches.OrderBy(a => a.SheetRow).ToList()
            : matches.OrderBy(SortKey, StringComparer.Ordinal).ToList();

        FindResults.ItemsSource = _found;
        FindEmptyText.IsVisible = _found.Count == 0;
        FindCountText.Text = _found.Count switch
        {
            0 => "",
            1 => "1 album",
            _ => string.IsNullOrEmpty(query)
                ? $"{_found.Count} albums · drag the rail to skim"
                : $"{_found.Count} albums match",
        };

        BuildRail();
    }

    /// <summary>
    /// What an album files under. Names sort the way a record shelf does, which is what
    /// <see cref="NumberedList.Normalize"/> already gives us: case and accents flattened, and a
    /// leading "the" dropped, so The Beatles sits under B rather than in a T three hundred long.
    /// </summary>
    private string SortKey(AlbumEntry album) =>
        NumberedList.Normalize(_findSort == FindSort.Artist ? album.Artist : album.Title);

    /// <summary>
    /// The stop an album falls under on the rail: its initial for the two name orders, its hundred
    /// for list order. Anything not starting with a letter files under "#", as it does on iOS.
    /// </summary>
    private string RailStopFor(AlbumEntry album)
    {
        if (_findSort == FindSort.Number)
            return int.TryParse(album.Number, out int n) ? $"{(n - 1) / 100 * 100 + 1}" : "1";

        string key = SortKey(album);
        char first = key.Length > 0 ? char.ToUpperInvariant(key[0]) : '#';
        return first is >= 'A' and <= 'Z' ? first.ToString() : "#";
    }

    /// <summary>
    /// Lays out the rail for what's on screen — only stops that actually have rows, so dragging it
    /// never lands on nothing. A label is taken once, at its first row: titles that start with a
    /// symbol sort both before "A" and after "Z", and two "#" stops on one strip is a rail that
    /// reads as broken. Hidden when there's too little to be worth skimming.
    /// </summary>
    private void BuildRail()
    {
        _rail = new List<(string, int)>();
        var seen = new HashSet<string>();
        for (int i = 0; i < _found.Count; i++)
        {
            string stop = RailStopFor(_found[i]);
            if (!seen.Add(stop)) continue;
            _rail.Add((stop, i));
        }

        RailStack.Children.Clear();
        IndexRail.IsVisible = _rail.Count > 2;
        if (!IndexRail.IsVisible) return;

        foreach (var (label, _) in _rail)
        {
            RailStack.Children.Add(new TextBlock
            {
                Text = label,
                Classes = { "railLetter" },
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            });
        }
    }

    /// <summary>
    /// Grabs the pointer so a scrub that wanders off the 26pt gutter keeps scrubbing instead of
    /// being handed to the list underneath.
    /// </summary>
    private void OnRailPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Pointer.Capture(IndexRail);
        Scrub(e);
    }

    private void OnRailMoved(object? sender, PointerEventArgs e)
    {
        if (e.GetCurrentPoint(IndexRail).Properties.IsLeftButtonPressed) Scrub(e);
    }

    private void OnRailReleased(object? sender, PointerReleasedEventArgs e) => RailBubble.IsVisible = false;

    private void OnRailCaptureLost(object? sender, PointerCaptureLostEventArgs e) => RailBubble.IsVisible = false;

    /// <summary>
    /// Scrubbing the rail. Which stop you're on comes from where your finger is down the strip
    /// rather than from which label it happens to be over, so a drag runs continuously instead of
    /// sticking in the gaps between 10pt letters.
    /// </summary>
    private void Scrub(PointerEventArgs e)
    {
        if (_rail.Count == 0 || RailStack.Bounds.Height <= 0) return;

        // Measured against the letter stack rather than the gutter: GetPosition undoes the
        // Viewbox's scale for us, so this stays right however far the strip has been shrunk.
        double y = e.GetPosition(RailStack).Y;
        int at = (int)(y / RailStack.Bounds.Height * _rail.Count);
        at = Math.Clamp(at, 0, _rail.Count - 1);

        var (label, index) = _rail[at];
        RailBubbleText.Text = label;
        RailBubble.IsVisible = true;

        if (index >= 0 && index < _found.Count) FindResults.ScrollIntoView(_found[index]);
        e.Handled = true;
    }

    /// <summary>
    /// Jumps the card to the tapped album, whatever it's rated now, and puts the results away so
    /// the rating tiles are back under your thumb.
    /// </summary>
    private void OnPickFound(object? sender, SelectionChangedEventArgs e)
    {
        if (_busy || _session is null || FindResults.SelectedItem is not AlbumEntry album) return;

        FindResults.SelectedItem = null;
        CloseFind();

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
        BrowseButton.IsEnabled = on && _session is not null;
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
