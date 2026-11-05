namespace _1001AlbumHelper;

/// <summary>
/// Finds a genre for an album, trying the sources in the order they're best at it:
/// <list type="number">
/// <item><b>Discogs</b> — specific styles first ("Bluegrass, Country"), the vocabulary the rest of
/// the lists already use. Misses obscure releases and anything filed under an odd name.</item>
/// <item><b>Apple Music's catalog</b> — every album it stocks has a primary genre, so it catches
/// most of what Discogs missed. Broader ("Country"), and named its own way, so it's mapped onto the
/// Discogs words (<see cref="FromAppleMusic"/>) rather than splitting the charts in two.</item>
/// <item><b>The artist's other albums</b> on the lists — a last resort, but a good one: an artist
/// rarely changes genre between two records on the same list.</item>
/// </list>
/// One source failing (offline, rate-limited, no match) just hands over to the next.
/// </summary>
public static class AlbumGenres
{
    /// <summary>Where a genre came from, for the log.</summary>
    public enum Source { Discogs, AppleMusic, Artist }

    /// <param name="known">Genres already known on the lists, as (artist, genre) pairs.</param>
    /// <param name="discogsGenre">A Discogs genre the caller already has from a lookup it made anyway
    /// (the shortlist fetches years from the same call), to save asking twice.</param>
    public static async Task<(string Genre, Source From)?> FindAsync(
        string title, string artist, DiscogsApiClient? discogs,
        IEnumerable<(string Artist, string Genre)> known, string? discogsGenre = null,
        CancellationToken ct = default)
    {
        string? genre = discogsGenre;
        if (string.IsNullOrWhiteSpace(genre) && discogs is not null)
        {
            try { genre = (await discogs.FindAlbumAsync(title, artist, ct))?.Genre; }
            catch (OperationCanceledException) { throw; }
            catch { /* next source */ }
        }
        if (!string.IsNullOrWhiteSpace(genre)) return (genre.Trim(), Source.Discogs);

        try
        {
            var release = await AppleMusicCatalog.FindAlbumAsync(artist, title, ct);
            var mapped = FromAppleMusic(release?.Genre);
            if (mapped is not null) return (mapped, Source.AppleMusic);
        }
        catch (OperationCanceledException) { throw; }
        catch { /* next source */ }

        var byArtist = FromArtist(known, artist);
        return byArtist is null ? null : (byArtist, Source.Artist);
    }

    /// <summary>
    /// Apple Music's genre name in the words Discogs uses, so one record isn't "Hip-Hop/Rap" and the
    /// next "Hip Hop". Names that already match pass through. Null for no genre, or for Apple's
    /// catch-alls that say nothing ("Music").
    /// </summary>
    public static string? FromAppleMusic(string? appleGenre)
    {
        string g = (appleGenre ?? "").Trim();
        if (g.Length == 0 || g.Equals("Music", StringComparison.OrdinalIgnoreCase)) return null;
        return g.ToLowerInvariant() switch
        {
            "hip-hop/rap" or "hip-hop" or "rap" => "Hip Hop",
            "r&b/soul" or "soul" or "r&b" => "Funk / Soul",
            "alternative" => "Alternative Rock",
            "metal" => "Heavy Metal",
            "dance" or "electronica" => "Electronic",
            "soundtrack" => "Stage & Screen",
            "christian & gospel" or "gospel" => "Gospel",
            "worldwide" => "World",
            "singer/songwriter" => "Singer/Songwriter",
            _ => g,
        };
    }

    /// <summary>
    /// The genre the artist's other albums on the lists most often lead with, or null if none of
    /// theirs has one. Only the first-listed tag counts — a Discogs string leads with its most
    /// specific style (see <see cref="AlbumAnalytics.GenresOf"/>) — and only an exact artist match, since borrowing a
    /// genre from the wrong act is worse than leaving it blank.
    /// </summary>
    public static string? FromArtist(IEnumerable<(string Artist, string Genre)> known, string artist) =>
        known
            .Where(k => k.Genre.Trim().Length > 0 && NumberedList.Matches(k.Artist, artist))
            // SplitTags, not a bare split: "Folk, World, & Country" is one genre, not "Folk".
            .Select(k => AlbumAnalytics.SplitTags(k.Genre).FirstOrDefault() ?? "")
            .Where(g => g.Length > 0)
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
}
