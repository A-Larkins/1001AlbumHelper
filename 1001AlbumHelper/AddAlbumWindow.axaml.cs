using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>Adds an album to the replacements list, placed by year and renumbered.</summary>
public partial class AddAlbumWindow : Window
{
    private bool _busy;

    // Null when no Discogs token is configured: the form still works, just without lookup.
    private readonly DiscogsApiClient? _discogs = DiscogsApiClient.TryCreate();

    public AddAlbumWindow()
    {
        InitializeComponent();
        Opened += (_, _) => TitleBox.Focus();

        if (_discogs is null)
        {
            LookupHint.Text = "Album lookup is off — add a Discogs token to appsettings.json to "
                            + "have the artist and year filled in for you.";
            return;
        }

        LookupHint.Text = "Start typing an album and pick a match — the artist and year fill "
                        + "themselves in. Naming the artist first narrows the search to them.";

        TitleBox.AsyncPopulator = PopulateAlbumsAsync;
        TitleBox.SelectionChanged += OnAlbumSuggestionPicked;
        // Keep the box holding just the album name, not the "Title — Artist (Year)" label.
        TitleBox.ItemSelector = (_, item) => item is AlbumSuggestion s ? s.Title : item?.ToString() ?? "";

        ArtistBox.AsyncPopulator = PopulateArtistsAsync;
    }

    // ----- Discogs lookup ----------------------------------------------------

    private async Task<IEnumerable<object>> PopulateAlbumsAsync(string? query, CancellationToken ct)
    {
        // Whatever is in the artist box narrows the search to that artist, so "ok" with
        // "Radiohead" named finds OK Computer rather than every album starting "ok".
        string artist = ArtistBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(query) && artist.Length == 0)
            return Array.Empty<object>();

        var matches = await _discogs!.SearchAlbumsAsync(query ?? "", artist, ct);
        return matches;
    }

    private async Task<IEnumerable<object>> PopulateArtistsAsync(string? query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<object>();
        var names = await _discogs!.SearchArtistsAsync(query, ct);
        return names.Cast<object>();
    }

    /// <summary>Picking an album fills in the two fields you'd otherwise have to go look up.</summary>
    private void OnAlbumSuggestionPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (TitleBox.SelectedItem is not AlbumSuggestion pick) return;

        ArtistBox.Text = pick.Artist;
        YearBox.Text = pick.Year;
        LookupHint.Text = string.IsNullOrEmpty(pick.Year)
            ? $"Discogs has no year for “{pick.Title}” — fill it in yourself."
            : $"Filled in from Discogs: {pick.Artist}, {pick.Year}.";
    }

    private async void OnAdd(object? sender, RoutedEventArgs e) => await AddAsync();

    private async Task AddAsync()
    {
        if (_busy) return;

        string title = TitleBox.Text?.Trim() ?? "";
        string artist = ArtistBox.Text?.Trim() ?? "";
        string yearText = YearBox.Text?.Trim() ?? "";

        if (title.Length == 0) { Fail("Enter the album title."); TitleBox.Focus(); return; }
        if (artist.Length == 0) { Fail("Enter the artist."); ArtistBox.Focus(); return; }
        if (!int.TryParse(yearText, out int year) || year < 1900 || year > DateTime.Now.Year + 1)
        {
            Fail("Enter a four-digit year.");
            YearBox.Focus();
            return;
        }

        _busy = true;
        AddButton.IsEnabled = false;
        StatusText.Text = "Adding…";

        try
        {
            var result = await Operations.AddReplacementAlbumAsync(title, artist, year);
            switch (result.Outcome)
            {
                case Operations.AddOutcome.Added:
                    StatusText.Text = $"✓ Added “{title}” ({year}) at #{result.Position}. "
                                    + "The list was renumbered."
                                    + (result.Warning is null ? "" : $"\n{result.Warning}");
                    // Clear the pick first: without it, choosing the same album again wouldn't
                    // register as a change and the artist/year would never refill.
                    TitleBox.SelectedItem = null;
                    TitleBox.Text = "";
                    ArtistBox.Text = "";
                    YearBox.Text = "";
                    TitleBox.Focus();
                    break;

                case Operations.AddOutcome.AlreadyInReplacements:
                    StatusText.Text = $"⚠️ Not added. {result.Detail}";
                    break;

                case Operations.AddOutcome.AlreadyIn1001:
                    StatusText.Text = $"⚠️ Not added. {result.Detail}";
                    break;

                case Operations.AddOutcome.NotConfigured:
                    StatusText.Text = $"✗ {result.Detail}";
                    break;

                default:
                    StatusText.Text = $"✗ Couldn't add “{title}”: {result.Detail}";
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"✗ {ex.Message}";
        }
        finally
        {
            _busy = false;
            AddButton.IsEnabled = true;
        }
    }

    private void Fail(string message) => StatusText.Text = $"✗ {message}";

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
