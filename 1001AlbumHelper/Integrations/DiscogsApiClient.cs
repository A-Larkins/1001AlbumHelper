using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace _1001AlbumHelper;

public class DiscogsApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _token;

    public DiscogsApiClient()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "1001AlbumHelper/1.0 (https://github.com/alarks/1001AlbumHelper)");

        // Read the copy next to the executable first, then let the live copy in the data folder
        // override it — same layering Operations uses, so editing appsettings.json takes effect
        // without a rebuild (matters for the packaged .app, whose baked-in copy would be stale).
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
            .AddJsonFile(Path.Combine(Operations.ProjectDir, "appsettings.json"), optional: true)
            .Build();

        _token = config["Discogs:Token"] ?? "";
        if (string.IsNullOrEmpty(_token) || _token == "YOUR_DISCOGS_TOKEN")
        {
            throw new Exception("Discogs token not found in appsettings.json");
        }
    }

    /// <summary>
    /// A client, or null when no Discogs token is configured. For callers that treat lookup as a
    /// nicety rather than a requirement, so an unconfigured token degrades instead of throwing.
    /// </summary>
    public static DiscogsApiClient? TryCreate()
    {
        try { return new DiscogsApiClient(); }
        catch { return null; }
    }

    public async Task<List<Album>> FetchAlbumsFromListAsync(string listId)
    {
        var albums = new List<Album>();

        try
        {
            string url = $"https://api.discogs.com/lists/{listId}?token={_token}";
            Console.WriteLine("Fetching list from Discogs API...");

            var response = await _httpClient.GetStringAsync(url);
            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            List<JsonElement> items = new List<JsonElement>();
            items = root.GetProperty("items").EnumerateArray().ToList();
            
            List<string> ids = items.Select(item => item.GetProperty("id").GetInt32().ToString()).ToList();     

            Console.WriteLine($"Fetching {ids.Count} releases (60 requests/min with auth)...");
            int estimatedMinutes = (int)Math.Ceiling(ids.Count / 60.0);
            Console.WriteLine($"⚠️  This will take approximately {estimatedMinutes} minutes to complete.\n");

            int count = 0;
            foreach(string id in ids)
            {
                count++;
                string masterUrl = $"https://api.discogs.com/masters/{id}?token={_token}";
                var masterResponse = await _httpClient.GetStringAsync(masterUrl);
                var masterDoc = JsonDocument.Parse(masterResponse);
                var masterRoot = masterDoc.RootElement;

                string year = "";
                if (masterRoot.TryGetProperty("year", out JsonElement yearEl) && yearEl.GetInt32() > 0)
                {
                    year = yearEl.GetInt32().ToString();
                }

                if (count % 50 == 0)
                    Console.WriteLine($"Progress: {count}/{ids.Count}");

                // 60 req/min = 1 per second
                await Task.Delay(1000);

                foreach (var item in items)
                {
                    int itemId = item.GetProperty("id").GetInt32();
                    if (itemId.ToString() == id)
                    {
                        var (artist, albumName) = SplitDisplayTitle(
                            item.GetProperty("display_title").GetString() ?? "");

                        // Handle self-titled albums
                        if (!string.IsNullOrEmpty(artist) && albumName.Equals(artist, StringComparison.OrdinalIgnoreCase))
                        {
                            if (artist.Equals("The Beatles", StringComparison.OrdinalIgnoreCase))
                                albumName = "The Beatles (White Album)";
                            else if (artist.Equals("Metallica", StringComparison.OrdinalIgnoreCase))
                                albumName = "Metallica (Black Album)";
                            else
                                albumName = $"{albumName} (self titled)";
                        }

                        Album album = new Album
                        {
                            AlbumName = albumName,
                            Artist = artist,
                            Year = year
                        };

                        albums.Add(album);
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Successfully fetched {albums.Count} albums!");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Error fetching from Discogs API: {ex.Message}");
            Console.WriteLine("Make sure you have internet connection.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        return albums;
    }

    // ----- Lookup / autocomplete ---------------------------------------------

    /// <summary>
    /// Albums matching <paramref name="query"/>, best match first, optionally narrowed to one
    /// artist. Returns an empty list rather than throwing when the lookup fails — a search box
    /// that quietly finds nothing is better than one that tears down the form.
    /// </summary>
    /// <remarks>
    /// Discogs returns one "master" per distinct release, so a well-known album comes back several
    /// times over: the original, later reissues, and bootlegs, each with its own year. We collapse
    /// those to one entry per artist+title and keep the copy the most Discogs users own, because
    /// that is reliably the original pressing — which is the year this app wants. Picking the
    /// *earliest* year instead would look equally sensible and be subtly wrong: it hands you
    /// Blue Train as 1957 (a recording-date master) rather than its 1958 release.
    /// </remarks>
    public async Task<List<AlbumSuggestion>> SearchAlbumsAsync(
        string query, string? artist = null, CancellationToken ct = default)
    {
        query = query.Trim();
        artist = artist?.Trim();
        if (query.Length == 0 && string.IsNullOrEmpty(artist)) return new List<AlbumSuggestion>();

        // format=album keeps singles and EPs out of a list that is only ever about albums.
        var url = $"https://api.discogs.com/database/search?type=master&format=album&per_page=50&token={_token}";
        if (query.Length > 0) url += $"&release_title={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrEmpty(artist)) url += $"&artist={Uri.EscapeDataString(artist)}";

        var results = await GetSearchResultsAsync(url, ct);
        return RankAlbumResults(results);
    }

    /// <summary>
    /// Turns raw Discogs search results into the shortlist the dropdown shows: bootlegs and box
    /// sets dropped, one entry per artist+title, best-known copy of each kept, most-owned first.
    /// Split out from the request so it can be tested without going near the network.
    /// </summary>
    public static List<AlbumSuggestion> RankAlbumResults(IEnumerable<JsonElement> results, int take = 12) =>
        results
            .Select(ToSuggestion)
            .Where(s => s is not null)
            .Select(s => s!)
            .GroupBy(s => (Norm(s.Artist), Norm(s.Title)))
            .Select(g => g.OrderByDescending(s => s.Have).First())
            .OrderByDescending(s => s.Have)
            .Take(take)
            .ToList();

    /// <summary>
    /// The one album that best answers "what year is this?", trying progressively looser searches
    /// until one produces a match that verifies.
    /// <para>
    /// <see cref="SearchAlbumsAsync"/> is built for a dropdown someone is watching, so it asks
    /// Discogs a single precise question and shows whatever comes back. That precision is wrong
    /// for an unattended lookup, where each filter is its own way to find nothing: <c>format=album</c>
    /// excludes EPs outright, <c>artist=</c> misses records credited to a stage name or a one-off
    /// billing, and <c>release_title=</c> wants the edition's exact name rather than the album's.
    /// Each rung below drops one of those constraints.
    /// </para>
    /// <para>
    /// Loosening a search is only safe if the answer is checked, so every rung verifies what came
    /// back — see <see cref="TitlesLineUp"/> — and the last, which searches on title alone, also
    /// requires the artist to be recognisable. Without that it would happily date your album from
    /// an unrelated record that happens to share its name.
    /// </para>
    /// </summary>
    public async Task<AlbumSuggestion?> FindAlbumAsync(
        string title, string artist, CancellationToken ct = default)
    {
        title = title.Trim();
        artist = artist.Trim();
        if (title.Length == 0) return null;

        string t = Uri.EscapeDataString(title);
        string a = Uri.EscapeDataString(artist);

        // Discogs has already constrained these to the artist, so the title is the open question.
        bool ByTitle(AlbumSuggestion s) => TitlesLineUp(s.Title, title);

        // A general query is not bound to the artist, and a title alone cannot tell an edition of
        // an album from a different record that merely starts the same way — "Purple" is as much a
        // prefix of "Purple Rain" as "This One's For You" is of that album's deluxe. Checking the
        // artist too is what separates the two cases.
        bool ByBoth(AlbumSuggestion s) => ByTitle(s) && ArtistsOverlap(s.Artist, artist);

        var rungs = new (string Url, Func<AlbumSuggestion, bool> Accept)[]
        {
            // As before: the precise question, which answers most albums on the first ask.
            ($"&format=album&release_title={t}&artist={a}", ByTitle),

            // Without format: EPs, mini-albums and anything Discogs doesn't file as an album.
            ($"&release_title={t}&artist={a}", ByTitle),

            // A general query rather than field matches — forgiving of punctuation and of an
            // edition's name differing from the album's. Naming the artist steers the search
            // without binding it, so the result still has to be checked against them.
            ($"&q={t}+{a}", ByBoth),

            // Title alone, for records credited to a name the playlist doesn't use.
            ($"&q={t}", ByBoth),
        };

        // A playlist names the pressing it holds; Discogs catalogues the album. When the two
        // differ it's usually edition furniture, so ask again without it.
        string bare = BareTitle(title);
        if (!string.Equals(bare, title, StringComparison.Ordinal))
        {
            string b = Uri.EscapeDataString(bare);
            rungs = rungs.Append(($"&release_title={b}&artist={a}", ByTitle))
                         .Append(($"&q={b}+{a}", ByBoth))
                         .ToArray();
        }

        for (int i = 0; i < rungs.Length; i++)
        {
            // Every rung spends from the same 60-a-minute budget as the caller's own pacing, so a
            // hard-to-find album doesn't get to burn four requests in one second.
            if (i > 0) await Task.Delay(TimeSpan.FromMilliseconds(350), ct);

            var url = $"https://api.discogs.com/database/search?type=master&per_page=50"
                    + $"&token={_token}{rungs[i].Url}";

            var found = RankAlbumResults(await GetSearchResultsAsync(url, ct))
                .FirstOrDefault(s => s.Year.Length > 0 && rungs[i].Accept(s));

            if (found is not null) return found;
        }

        return null;
    }

    /// <summary>
    /// Whether two album titles name the same record, allowing for one being an edition of the
    /// other: the shorter title has to run as a whole-word prefix of the longer.
    /// <para>
    /// That accepts "This One's For You" for "This One's for You Too" while refusing "Four" for
    /// "Fourty Licks", which a plain substring test would wave through — prefixes are compared a
    /// word at a time, so a title can only be extended, never cut mid-word.
    /// </para>
    /// </summary>
    public static bool TitlesLineUp(string a, string b)
    {
        var x = TitleWords(a);
        var y = TitleWords(b);
        if (x.Length == 0 || y.Length == 0) return false;

        var (shorter, longer) = x.Length <= y.Length ? (x, y) : (y, x);
        return shorter.SequenceEqual(longer.Take(shorter.Length));
    }

    /// <summary>
    /// A title reduced to the words that identify it.
    /// <para>
    /// "and" and "the" come and go between catalogues — Discogs presses "Kirk Franklin &amp; The
    /// Family" where a playlist writes "Kirk Franklin and the Family" — and normalising drops the
    /// ampersand entirely, leaving a stray "and" to knock every later word out of step. Since they
    /// carry no identity, they go.
    /// </para>
    /// </summary>
    private static string[] TitleWords(string title) =>
        NumberedList.Normalize(title)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("and" or "the"))
            .ToArray();

    /// <summary>
    /// A title with its edition furniture removed: a trailing "(Live)" or "[Remastered]", and any
    /// subtitle after a colon. Used to ask Discogs about the album rather than the pressing.
    /// </summary>
    public static string BareTitle(string title)
    {
        var bare = Regex.Replace(title, @"\s*[\(\[][^\)\]]*[\)\]]\s*$", "").Trim();

        // Only cut at a colon when something substantial survives, so "Vs." and ": Reprise" keep
        // whatever name they have.
        int colon = bare.IndexOf(':');
        if (colon > 2) bare = bare[..colon].Trim();

        return bare.Length > 0 ? bare : title.Trim();
    }

    /// <summary>
    /// Whether two artist credits plausibly name the same act, by sharing a substantial word.
    /// <para>
    /// Discogs bills albums as they were pressed, which is often not what a playlist calls them:
    /// King of America is credited to "The Costello Show Featuring The Attractions And
    /// Confederates". Sharing "costello" is the signal. Short words are ignored so that "and",
    /// "the" or a stray initial can't make two unrelated acts look related.
    /// </para>
    /// </summary>
    public static bool ArtistsOverlap(string a, string b)
    {
        var x = NumberedList.Normalize(a).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4).ToHashSet();
        var y = NumberedList.Normalize(b).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4);

        return x.Count > 0 && y.Any(x.Contains);
    }

    /// <summary>Artist names matching <paramref name="query"/>, most-collected first.</summary>
    public async Task<List<string>> SearchArtistsAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0) return new List<string>();

        var url = $"https://api.discogs.com/database/search?type=artist&per_page=20"
                + $"&q={Uri.EscapeDataString(query)}&token={_token}";

        var results = await GetSearchResultsAsync(url, ct);

        return results
            .Select(r => CleanArtist(r.TryGetProperty("title", out var t) ? t.GetString() ?? "" : ""))
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    private async Task<List<JsonElement>> GetSearchResultsAsync(string url, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(response);
            return doc.RootElement.TryGetProperty("results", out var results)
                // Clone: the elements have to outlive the JsonDocument we dispose here.
                ? results.EnumerateArray().Select(e => e.Clone()).ToList()
                : new List<JsonElement>();
        }
        catch (OperationCanceledException)
        {
            throw; // A superseded keystroke, not a failure — let the caller drop it.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Discogs lookup failed: {ex.Message}");
            return new List<JsonElement>();
        }
    }

    /// <summary>Maps one search result, or null if it isn't an album we'd ever want to suggest.</summary>
    private static AlbumSuggestion? ToSuggestion(JsonElement result)
    {
        var formats = result.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.Array
            ? f.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
            : new List<string>();

        // Bootlegs carry the real album's name and year but are never the record you mean, and
        // box sets are a packaging of albums rather than one.
        if (formats.Any(x => x.Contains("Unofficial", StringComparison.OrdinalIgnoreCase)
                          || x.Contains("Box Set", StringComparison.OrdinalIgnoreCase)))
            return null;

        var displayTitle = result.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var (artist, title) = SplitDisplayTitle(displayTitle);
        if (artist.Length == 0 || title.Length == 0) return null;

        var year = result.TryGetProperty("year", out var y) ? y.GetString() ?? "" : "";
        var have = result.TryGetProperty("community", out var c)
                && c.TryGetProperty("have", out var h) && h.TryGetInt32(out int haveCount)
            ? haveCount
            : 0;

        return new AlbumSuggestion(title, artist, year, have);
    }

    /// <summary>
    /// Splits Discogs' "Artist - Album" display title. Splits on the *first* separator only, so
    /// albums with a dash of their own ("Bitches Brew - Live") keep their full name.
    /// </summary>
    public static (string Artist, string Title) SplitDisplayTitle(string displayTitle)
    {
        if (string.IsNullOrWhiteSpace(displayTitle)) return ("", "");

        int split = displayTitle.IndexOf(" - ", StringComparison.Ordinal);
        if (split < 0) return ("", displayTitle.Trim());

        return (CleanArtist(displayTitle[..split]), displayTitle[(split + 3)..].Trim());
    }

    /// <summary>
    /// Strips Discogs' bookkeeping from an artist name: a trailing "*" marks a name variation,
    /// and a trailing "(2)" disambiguates same-named artists. Neither belongs in the list.
    /// </summary>
    public static string CleanArtist(string artist) =>
        Regex.Replace(artist.Trim(), @"\s*\(\d+\)$", "").TrimEnd('*').Trim();

    private static string Norm(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
}
