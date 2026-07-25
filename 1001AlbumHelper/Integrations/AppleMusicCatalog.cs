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
        return FindBestMatch(ParseSearchResults(json), artist, title);
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
    /// else the first result. Titles are compared with the same loose rule the Discogs lookup uses,
    /// so a catalogue "(Deluxe Edition)" / "(Live)" still lines up with the plain album name.
    /// </summary>
    public static AppleMusicAlbum? FindBestMatch(List<AppleMusicAlbum> candidates, string artist, string title)
    {
        var both = candidates.FirstOrDefault(c =>
            DiscogsApiClient.TitlesLineUp(c.CollectionName, title) && NumberedList.Matches(c.ArtistName, artist));
        if (both is not null) return both;

        var byTitle = candidates.FirstOrDefault(c => DiscogsApiClient.TitlesLineUp(c.CollectionName, title));
        return byTitle ?? candidates.FirstOrDefault();
    }
}
