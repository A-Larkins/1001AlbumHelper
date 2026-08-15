using System.Text.Json;

namespace _1001AlbumHelper;

/// <summary>One album found in the Apple Music / iTunes catalog.</summary>
/// <param name="ArtworkUrl">
/// Cover art, as the 100×100 thumbnail iTunes reports. Blank when the row carried no artwork.
/// Ask for a bigger copy with <see cref="AppleMusicCatalog.ArtworkAtSize"/> rather than using this
/// URL directly — 100px looks like mush on a rating card.
/// </param>
public sealed record AppleMusicAlbum(
    long CollectionId,
    string CollectionName,
    string ArtistName,
    int TrackCount,
    string ArtworkUrl = "");

/// <summary>
/// Looks albums up in the Apple Music catalog via the public iTunes Search API
/// (no auth, no account needed). The returned <see cref="AppleMusicAlbum.CollectionId"/> is the
/// store id an on-device playlist can be built from. Cross-platform — the parsing and matching are
/// pure so they can be unit-tested without hitting the network.
/// </summary>
public static class AppleMusicCatalog
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Best catalog match for an album, or null if nothing plausible was found.</summary>
    public static async Task<AppleMusicAlbum?> FindAlbumAsync(string artist, string title, CancellationToken ct = default)
    {
        var term = Uri.EscapeDataString($"{artist} {title}".Trim());
        var url = $"https://itunes.apple.com/search?media=music&entity=album&limit=15&term={term}";
        var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
        var match = FindBestMatch(ParseSearchResults(json), artist, title);
        if (match is not null) return match;

        // Apple's /search relevance ranking can bury even famous, definitely-in-the-catalog albums —
        // e.g. it returns zero results for "Nine Inch Nails The Downward Spiral" combined, and never
        // surfaces the 1994 studio album at all for "Nine Inch Nails" alone, crowded out by newer
        // releases, singles, and tribute covers. /lookup by the artist's own id isn't a relevance
        // search — it just lists their catalog — so it finds albums /search can't.
        return await FindInArtistCatalogAsync(artist, title, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Falls back to the artist's full album catalog via <c>/lookup</c> (not a relevance search) when
    /// <c>/search</c> found nothing plausible.
    /// </summary>
    private static async Task<AppleMusicAlbum?> FindInArtistCatalogAsync(string artist, string title, CancellationToken ct)
    {
        long? artistId = await FindArtistIdAsync(artist, ct).ConfigureAwait(false);
        if (artistId is null) return null;

        var url = $"https://itunes.apple.com/lookup?id={artistId}&entity=album&limit=200";
        var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
        return FindBestMatch(ParseSearchResults(json), artist, title);
    }

    /// <summary>The iTunes catalog id for an artist's best-matching name, or null if none was found.</summary>
    private static async Task<long?> FindArtistIdAsync(string artist, CancellationToken ct)
    {
        var term = Uri.EscapeDataString(artist);
        var url = $"https://itunes.apple.com/search?media=music&entity=musicArtist&limit=1&term={term}";
        var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return null;

        return results[0].TryGetProperty("artistId", out var id) && id.TryGetInt64(out long artistId)
            ? artistId
            : null;
    }

    /// <summary>Pulls the album rows out of an iTunes Search API response.</summary>
    public static List<AppleMusicAlbum> ParseSearchResults(string json)
    {
        var albums = new List<AppleMusicAlbum>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
            return albums;

        foreach (var r in results.EnumerateArray())
        {
            if (r.TryGetProperty("collectionId", out var cid) && cid.TryGetInt64(out long id))
            {
                // Missing trackCount means we can't tell whether it's the plain album or a padded
                // edition, so it loses to any candidate that does report one.
                int trackCount = r.TryGetProperty("trackCount", out var tc) && tc.TryGetInt32(out int t)
                    ? t
                    : int.MaxValue;

                albums.Add(new AppleMusicAlbum(
                    id,
                    r.TryGetProperty("collectionName", out var n) ? n.GetString() ?? "" : "",
                    r.TryGetProperty("artistName", out var a) ? a.GetString() ?? "" : "",
                    trackCount,
                    r.TryGetProperty("artworkUrl100", out var art) ? art.GetString() ?? "" : ""));
            }
        }
        return albums;
    }

    /// <summary>
    /// Picks the best candidate: among albums matching both title and artist, else among title-only
    /// matches, the one with the fewest tracks — else null. Titles are compared with the same loose
    /// rule the Discogs lookup uses, so a catalogue "(Deluxe Edition)" / "(Live)" still lines up with
    /// the plain album name.
    /// <para>
    /// Apple Music often lists several editions of the same album side by side — a deluxe reissue
    /// with a dozen bonus demos/remixes alongside the original release. Since those all line up on
    /// title and artist equally, the tie-break is track count: the original release is the one that
    /// wasn't padded out with extra material, so it's (almost always) the smallest.
    /// </para>
    /// <para>
    /// Deliberately never falls back to "just the first result" — iTunes's relevance ranking for a
    /// combined artist+title <c>/search</c> can still rank an unrelated album by the same artist above
    /// the actual match (e.g. a compilation outranking the real studio album), so an unmatched top hit
    /// is not a safe guess. Guessing wrong here means silently adding the wrong album to a playlist.
    /// </para>
    /// </summary>
    public static AppleMusicAlbum? FindBestMatch(List<AppleMusicAlbum> candidates, string artist, string title)
    {
        var both = candidates.Where(c =>
            DiscogsApiClient.TitlesLineUp(c.CollectionName, title) && NumberedList.Matches(c.ArtistName, artist));
        var smallestOfBoth = SmallestByTrackCount(both);
        if (smallestOfBoth is not null) return smallestOfBoth;

        var byTitle = candidates.Where(c => DiscogsApiClient.TitlesLineUp(c.CollectionName, title));
        return SmallestByTrackCount(byTitle);
    }

    private static AppleMusicAlbum? SmallestByTrackCount(IEnumerable<AppleMusicAlbum> candidates) =>
        candidates.OrderBy(c => c.TrackCount).FirstOrDefault();

    /// <summary>
    /// The same cover art at <paramref name="pixels"/> square. iTunes encodes the size in the file
    /// name (…/100x100bb.jpg) and will serve any size asked for, so a bigger copy is a string swap —
    /// there's no second API call for it. Returns null if there's no artwork to resize.
    /// </summary>
    public static string? ArtworkAtSize(string? artworkUrl, int pixels)
    {
        if (string.IsNullOrWhiteSpace(artworkUrl)) return null;

        // Only the size segment of the file name is replaced, so the rest of the path — which
        // includes a content hash — is left exactly as iTunes gave it.
        int slash = artworkUrl.LastIndexOf('/');
        if (slash < 0) return artworkUrl;

        string name = artworkUrl[(slash + 1)..];
        int x = name.IndexOf('x');
        int dot = name.LastIndexOf('.');
        if (x <= 0 || dot <= x) return artworkUrl;

        // "100x100bb.jpg" → keep the "bb" (or whatever crop code follows) and the extension.
        string suffix = name[(x + 1)..dot].TrimStart("0123456789".ToCharArray());
        return $"{artworkUrl[..slash]}/{pixels}x{pixels}{suffix}{name[dot..]}";
    }
}
