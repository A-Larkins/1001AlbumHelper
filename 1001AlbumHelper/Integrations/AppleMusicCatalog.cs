using System.Text.Json;

namespace _1001AlbumHelper;

/// <summary>One album found in the Apple Music / iTunes catalog.</summary>
public sealed record AppleMusicAlbum(long CollectionId, string CollectionName, string ArtistName);

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
                albums.Add(new AppleMusicAlbum(
                    id,
                    r.TryGetProperty("collectionName", out var n) ? n.GetString() ?? "" : "",
                    r.TryGetProperty("artistName", out var a) ? a.GetString() ?? "" : ""));
            }
        }
        return albums;
    }

    /// <summary>
    /// Picks the best candidate: an album matching both title and artist, else a title-only match,
    /// else null. Titles are compared with the same loose rule the Discogs lookup uses, so a catalogue
    /// "(Deluxe Edition)" / "(Live)" still lines up with the plain album name.
    /// <para>
    /// Deliberately never falls back to "just the first result" — iTunes's relevance ranking for a
    /// combined artist+title <c>/search</c> can still rank an unrelated album by the same artist above
    /// the actual match (e.g. a compilation outranking the real studio album), so an unmatched top hit
    /// is not a safe guess. Guessing wrong here means silently adding the wrong album to a playlist.
    /// </para>
    /// </summary>
    public static AppleMusicAlbum? FindBestMatch(List<AppleMusicAlbum> candidates, string artist, string title)
    {
        var both = candidates.FirstOrDefault(c =>
            DiscogsApiClient.TitlesLineUp(c.CollectionName, title) && NumberedList.Matches(c.ArtistName, artist));
        if (both is not null) return both;

        return candidates.FirstOrDefault(c => DiscogsApiClient.TitlesLineUp(c.CollectionName, title));
    }
}
