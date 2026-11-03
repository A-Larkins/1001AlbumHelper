using System.Text.RegularExpressions;
using System.Text.Json;

namespace _1001AlbumHelper;

/// <summary>One album found in the Apple Music / iTunes catalog.</summary>
/// <param name="ArtworkUrl">
/// Cover art, as the 100×100 thumbnail iTunes reports. Blank when the row carried no artwork.
/// Ask for a bigger copy with <see cref="AppleMusicCatalog.ArtworkAtSize"/> rather than using this
/// URL directly — 100px looks like mush on a rating card.
/// </param>
/// <param name="ViewUrl">The album's music.apple.com page, as iTunes reports it. Blank if absent.</param>
/// <param name="Cleaned">True for a radio edit of an explicit album — the version with words bleeped.</param>
public sealed record AppleMusicAlbum(
    long CollectionId,
    string CollectionName,
    string ArtistName,
    int TrackCount,
    string ArtworkUrl = "",
    string ViewUrl = "",
    bool Cleaned = false);

/// <summary>What kind of release a catalog listing is, judged from the furniture on its name.</summary>
public enum AlbumEdition
{
    /// <summary>The album under its own name, nothing added.</summary>
    Original,
    /// <summary>"(Remastered)", "(2011 Remaster)", "(Mono)" — the same record, cleaned up.</summary>
    Remaster,
    /// <summary>Some other tag that isn't obviously padding — "(Live)", "(Soundtrack)", "[Version 2]".</summary>
    Other,
    /// <summary>Deluxe, expanded, anniversary, bonus-track editions: the album plus extras.</summary>
    Deluxe,
    /// <summary>A " - Single" or " - EP" listing that happens to share the album's name.</summary>
    Single,
}

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
        var url = $"https://itunes.apple.com/search?media=music&entity=album&limit=25&term={term}";
        var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
        var candidates = ParseSearchResults(json);
        var match = FindBestMatch(candidates, artist, title);
        if (match is not null && IsSameTitle(match, title) && EditionOf(match.CollectionName) <= AlbumEdition.Remaster)
            return match;

        // Apple's /search relevance ranking can bury even famous, definitely-in-the-catalog albums —
        // e.g. it returns zero results for "Nine Inch Nails The Downward Spiral" combined, and never
        // surfaces the 1994 studio album at all for "Nine Inch Nails" alone, crowded out by newer
        // releases, singles, and tribute covers. /lookup by the artist's own id isn't a relevance
        // search — it just lists their catalog — so it finds albums /search can't. Asked too when
        // /search only turned up a deluxe edition or a subtitled near-miss, since the plain album
        // may be the one it buried: /search has no "Tical" at all, only "Tical 2000".
        try
        {
            candidates.AddRange(await ArtistCatalogAsync(artist, ct).ConfigureAwait(false));
        }
        catch (Exception) when (match is not null)
        {
            return match; // the edition /search found beats nothing
        }
        return FindBestMatch(candidates, artist, title);
    }

    /// <summary>
    /// The artist's full album catalog via <c>/lookup</c> — not a relevance search, so nothing in it
    /// is buried. Empty when no artist by that name is found.
    /// </summary>
    private static async Task<List<AppleMusicAlbum>> ArtistCatalogAsync(string artist, CancellationToken ct)
    {
        long? artistId = await FindArtistIdAsync(artist, ct).ConfigureAwait(false);
        if (artistId is null) return new();

        var url = $"https://itunes.apple.com/lookup?id={artistId}&entity=album&limit=200";
        var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
        return ParseSearchResults(json);
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
                    r.TryGetProperty("artworkUrl100", out var art) ? art.GetString() ?? "" : "",
                    r.TryGetProperty("collectionViewUrl", out var view) ? view.GetString() ?? "" : "",
                    r.TryGetProperty("collectionExplicitness", out var ex) && ex.GetString() == "cleaned"));
            }
        }
        return albums;
    }

    /// <summary>
    /// Picks which catalog listing is the album: the original release, else a remaster, and a
    /// deluxe edition only when Apple Music has nothing leaner — else null.
    /// <para>
    /// A listing qualifies when its name, with any edition furniture stripped from both sides
    /// (<see cref="SplitEdition"/>), is the same title as ours — so "Nevermind (Remastered)" and
    /// "In Utero - 30th Anniversary" qualify for "Nevermind" and "In Utero", while "Led Zeppelin II"
    /// does not for "Led Zeppelin". Only when nothing is the same title may a listing that adds a
    /// subtitle after a colon stand in ("Sgt. Pepper's…: Remix" for the list's shorter name) — never
    /// one that merely starts the same way, or "Tical 2000: Judgement Day" would pass for "Tical".
    /// A " - Single" or " - EP" sharing the album's name never qualifies.
    /// </para>
    /// <para>
    /// Among the qualifiers: the artist has to match where any listing's does; then the edition
    /// ranks them (<see cref="AlbumEdition"/> order); then the explicit version over a bleeped one;
    /// then fewest tracks, since of two same-named listings the padded one is the reissue.
    /// </para>
    /// <para>
    /// Deliberately never falls back to "just the first result" — iTunes's relevance ranking for a
    /// combined artist+title <c>/search</c> can rank an unrelated album by the same artist above the
    /// actual match, so an unmatched top hit is not a safe guess.
    /// </para>
    /// </summary>
    public static AppleMusicAlbum? FindBestMatch(List<AppleMusicAlbum> candidates, string artist, string title)
    {
        string wanted = SplitEdition(title).Bare;
        var albums = candidates.Where(c => EditionOf(c.CollectionName) != AlbumEdition.Single).ToList();

        var sameTitle = albums.Where(c => IsSameTitle(c, title)).ToList();
        var pool = sameTitle.Count > 0
            ? sameTitle
            : albums.Where(c => DiscogsApiClient.SameTitle(MainTitle(c.CollectionName), wanted)).ToList();

        var byArtist = pool.Where(c => ArtistMatches(c.ArtistName, artist)).ToList();
        if (byArtist.Count > 0) pool = byArtist;

        return pool
            .OrderBy(c => EditionOf(c.CollectionName))
            .ThenBy(c => c.Cleaned)
            .ThenBy(c => c.TrackCount)
            .FirstOrDefault();
    }

    /// <summary>Whether a listing is our album under its own name, edition tags aside on both sides.</summary>
    private static bool IsSameTitle(AppleMusicAlbum c, string title) =>
        DiscogsApiClient.SameTitle(SplitEdition(c.CollectionName).Bare, SplitEdition(title).Bare);

    /// <summary>
    /// Whether two album names are the same record allowing for edition furniture on either side —
    /// "Tago Mago" and "Tago Mago (2011 Remastered)", "Kind of Blue" and "Kind of Blue: Legacy" — but
    /// never a different record that merely starts the same way: "Tical" is not "Tical 2000:
    /// Judgement Day". The rule for deciding two playlist rows are one album, or what a removal takes.
    /// </summary>
    public static bool SameRelease(string a, string b) =>
        DiscogsApiClient.SameTitle(SplitEdition(a).Bare, SplitEdition(b).Bare)
        || DiscogsApiClient.SameTitle(MainTitle(a), MainTitle(b));

    /// <summary>A catalog name with edition tags and any ": subtitle" taken off.</summary>
    private static string MainTitle(string name)
    {
        string bare = SplitEdition(name).Bare;
        int colon = bare.IndexOf(':');
        return colon > 0 ? bare[..colon] : bare;
    }

    private static bool ArtistMatches(string a, string b) =>
        NumberedList.Matches(a, b) || DiscogsApiClient.ArtistsOverlap(a, b);

    /// <summary>
    /// Splits a catalog name into the album's own title and the edition tags hung on the end of it:
    /// any trailing "(…)" or "[…]" groups, and a trailing " - …" suffix when that suffix reads as an
    /// edition ("- 30th Anniversary", "- Single", "- 2011 Remaster") rather than part of the title.
    /// </summary>
    public static (string Bare, string Tags) SplitEdition(string name)
    {
        string bare = name.Trim();
        var tags = new List<string>();

        while (true)
        {
            var bracket = Regex.Match(bare, @"^(.+?)\s*[\(\[]([^\(\)\[\]]*)[\)\]]$");
            if (bracket.Success) { tags.Add(bracket.Groups[2].Value); bare = bracket.Groups[1].Value.Trim(); continue; }

            var dash = Regex.Match(bare, @"^(.+?)\s+[-–—]\s+([^-–—]+)$");
            if (dash.Success && IsEditionWording(dash.Groups[2].Value))
            {
                tags.Add(dash.Groups[2].Value); bare = dash.Groups[1].Value.Trim(); continue;
            }
            break;
        }
        return (bare, string.Join(" ", tags));
    }

    /// <summary>Which kind of release a catalog name describes — see <see cref="AlbumEdition"/>.</summary>
    public static AlbumEdition EditionOf(string name)
    {
        string tags = SplitEdition(name).Tags.ToLowerInvariant();
        if (tags.Length == 0) return AlbumEdition.Original;
        if (Regex.IsMatch(tags, @"\b(single|ep)\b")) return AlbumEdition.Single;
        if (Regex.IsMatch(tags, DeluxeWords)) return AlbumEdition.Deluxe;
        if (Regex.IsMatch(tags, RemasterWords)) return AlbumEdition.Remaster;
        return AlbumEdition.Other;
    }

    private const string DeluxeWords =
        @"\b(deluxe|expanded|anniversary|bonus|super|legacy|collector'?s?|special|complete|definitive|demos?|outtakes|box)\b";
    private const string RemasterWords = @"\b(remaster(ed)?|mono|stereo|reissue)\b";

    private static bool IsEditionWording(string suffix)
    {
        string s = suffix.ToLowerInvariant();
        return Regex.IsMatch(s, @"\b(single|ep|edition|version)\b")
               || Regex.IsMatch(s, DeluxeWords)
               || Regex.IsMatch(s, RemasterWords);
    }

    /// <summary>
    /// A link that opens the album's page in the Music app itself — the music.apple.com page with
    /// its scheme swapped for <c>music:</c>, which Music claims, so it doesn't detour through a
    /// browser. iTunes's tracking query string is dropped. Built from the id alone when iTunes gave
    /// no page; the US storefront in that fallback is the one iTunes answered for anyway, and Music
    /// redirects it to the listener's own.
    /// </summary>
    public static string MusicAppUrl(AppleMusicAlbum album)
    {
        string page = album.ViewUrl;
        int query = page.IndexOf('?');
        if (query >= 0) page = page[..query];

        return page.StartsWith("https://music.apple.com/", StringComparison.OrdinalIgnoreCase)
            ? "music://" + page["https://".Length..]
            : $"music://music.apple.com/us/album/{album.CollectionId}";
    }

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
