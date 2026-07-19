using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace _1001AlbumHelper;

/// <summary>
/// Wires an album/artist/year trio of inputs up to Discogs: both text boxes autocomplete, and
/// picking an album fills the artist and year in.
/// <para>
/// Shared by the two forms that take an album from the user, so they behave identically — the
/// same narrowing by artist, the same handling of a missing year, the same hint text.
/// </para>
/// </summary>
public static class AlbumLookup
{
    public const string OffHint =
        "Album lookup is off — add a Discogs token to appsettings.json to have the artist and "
      + "year filled in for you.";

    public const string OnHint =
        "Start typing an album and pick a match — the artist and year fill themselves in. "
      + "Naming the artist first narrows the search to them.";

    /// <summary>
    /// Attaches lookup to the given boxes.
    /// </summary>
    /// <param name="onPicked">
    /// Called once the artist and year have been filled in, for whatever the form wants to say
    /// about the pick.
    /// </param>
    /// <returns>The hint to show beneath the fields — which of the two depends on the token.</returns>
    public static string Attach(
        DiscogsApiClient? discogs,
        AutoCompleteBox titleBox,
        AutoCompleteBox artistBox,
        TextBox yearBox,
        Action<AlbumSuggestion>? onPicked = null)
    {
        if (discogs is null) return OffHint;

        titleBox.AsyncPopulator = async (query, ct) =>
        {
            // Whatever is in the artist box narrows the search to that artist, so "ok" with
            // "Radiohead" named finds OK Computer rather than every album starting "ok".
            string artist = artistBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(query) && artist.Length == 0)
                return Array.Empty<object>();

            return await discogs.SearchAlbumsAsync(query ?? "", artist, ct);
        };

        // Keep the box holding just the album name, not the "Title — Artist (Year)" label.
        titleBox.ItemSelector = (_, item) =>
            item is AlbumSuggestion s ? s.Title : item?.ToString() ?? "";

        titleBox.SelectionChanged += (_, _) =>
        {
            if (titleBox.SelectedItem is not AlbumSuggestion pick) return;
            artistBox.Text = pick.Artist;
            yearBox.Text = pick.Year;
            onPicked?.Invoke(pick);
        };

        artistBox.AsyncPopulator = async (query, ct) =>
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<object>();
            return (await discogs.SearchArtistsAsync(query, ct)).Cast<object>();
        };

        return OnHint;
    }

    /// <summary>What a form should say about an album that was just picked from the dropdown.</summary>
    public static string Picked(AlbumSuggestion pick) => string.IsNullOrEmpty(pick.Year)
        ? $"Discogs has no year for “{pick.Title}” — fill it in yourself."
        : $"Filled in from Discogs: {pick.Artist}, {pick.Year}.";
}
