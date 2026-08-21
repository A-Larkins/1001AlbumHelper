using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace _1001AlbumHelper;

/// <summary>
/// macOS implementation of <see cref="IApplePlaylistWriter"/>, driving the Music app over
/// AppleScript (<c>osascript</c>). The iPhone's MediaPlayer framework doesn't exist on the Mac, so
/// this is the Mac's route to the same two playlists.
///
/// <para>
/// Where iOS resolves an album to a store id via <see cref="AppleMusicCatalog"/> and adds it by id,
/// the Mac asks Music to <c>search</c> for it and duplicates the resulting tracks into the playlist.
/// With Sync Library on, that search covers the whole Apple Music catalog and not just what's
/// already been added — so obscure entries off the 1001 list resolve the same as they do on the
/// phone, and with no iTunes Search API call (so no rate limit — see PROJECT.md §7).
/// </para>
///
/// <para>
/// Two Music-app facts shape the code. Its AppleEvents are slow on a large library (a bare
/// <c>count</c> against ~33k tracks can exceed the default 60s), so every script wraps itself in
/// <c>with timeout</c> and the process gets a generous ceiling of its own. And a playlist holds
/// tracks, not albums — hence the search-then-filter-then-duplicate shape, and
/// <see cref="PlaylistTracks.CollapseToAlbums"/> on the way back out.
/// </para>
/// </summary>
public sealed class MusicAppPlaylistWriter : IApplePlaylistWriter
{
    /// <summary>How long to let one Music-app script run before giving up on it.</summary>
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Field separator inside a result row — tabs can't appear in an album or artist name.</summary>
    private const char Sep = '\t';

    /// <summary>Sentinel a script returns instead of rows when the named playlist doesn't exist.</summary>
    private const string NoPlaylist = "!!NOPLAYLIST";

    // ---------- Adding ----------

    public async Task<PlaylistOpResult> AddAlbumAsync(string playlistName, PlaylistEntry album)
    {
        List<LibraryTrack> hits;
        try
        {
            hits = await SearchLibraryAsync(album.Title).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new PlaylistOpResult(false, $"Music app lookup failed: {ex.Message}");
        }

        var selection = ChooseAlbum(hits, album.Artist, album.Title);
        if (selection.Chosen.Count == 0)
        {
            // Told apart, because they call for different things: an album Music has never heard of
            // is a lookup to fix, one Music has entirely withdrawn is nothing the app can do about.
            return selection.Unavailable.Count > 0
                ? new PlaylistOpResult(false, $"Apple Music no longer has a playable copy of {album.Title}.")
                : new PlaylistOpResult(false, $"Not found in Music: {album.Title}");
        }

        try
        {
            var added = await DuplicateIntoPlaylistAsync(playlistName, album.Title, selection.Chosen.ToList());
            if (added == NoPlaylistResult)
                return new PlaylistOpResult(false, $"No Music playlist named “{playlistName}” — create it in Music first.");
            if (added == 0)
                return new PlaylistOpResult(false, $"Music didn't add any tracks for {album.Title}.");

            // A short album is worth a word: silently adding 26 of 28 tracks looks like the app
            // half-failed, when in fact Apple Music has withdrawn those two everywhere.
            string missing = selection.Unavailable.Count == 0
                ? ""
                : $" ({selection.Unavailable.Count} not available in Apple Music: "
                  + string.Join(", ", selection.Unavailable.Select(t => t.Name)) + ")";

            return new PlaylistOpResult(true, $"Added {added} track{(added == 1 ? "" : "s")} to {playlistName}{missing}");
        }
        catch (Exception ex)
        {
            return new PlaylistOpResult(false, $"Couldn't add: {ex.Message}");
        }
    }

    /// <summary>
    /// Picks which of the search hits actually is the wanted album, and which copy of each of its
    /// songs to add.
    /// <para>
    /// The hits are grouped into albums and handed to the very same
    /// <see cref="AppleMusicCatalog.FindBestMatch"/> the iPhone uses, so both apps resolve an album
    /// identically — including preferring the original release over a padded "(Deluxe Edition)"
    /// reissue sitting beside it in the catalog. The group's index rides along in the record's id
    /// field purely so the winner can be mapped back to its tracks.
    /// </para>
    /// <para>
    /// Each group is reduced to one copy per song first (see
    /// <see cref="PlaylistTracks.PreferPlayable"/>), because Apple Music stocks some albums twice
    /// under identical names — nothing in the metadata separates them — and the copies aren't
    /// always equally playable. Reducing before the match matters as well as after: an album listed
    /// twice would otherwise count double, and could lose the fewest-tracks tie-break to a deluxe
    /// edition that only appears once.
    /// </para>
    /// </summary>
    private static TrackSelection ChooseAlbum(List<LibraryTrack> hits, string artist, string title)
    {
        var groups = hits
            .GroupBy(t => $"{NumberedList.Normalize(t.Album)}|{NumberedList.Normalize(t.AlbumArtist)}")
            .Select(g => PlaylistTracks.PreferPlayable(g))
            .Where(s => s.Chosen.Count > 0 || s.Unavailable.Count > 0)
            .ToList();

        var candidates = groups
            .Select((s, i) =>
            {
                var first = s.Chosen.Count > 0 ? s.Chosen[0] : s.Unavailable[0];
                // Counted on the songs the album actually has, playable or not, so a couple of
                // withdrawn tracks can't make a deluxe edition look like the leaner original.
                return new AppleMusicAlbum(i, first.Album, first.AlbumArtist,
                                           s.Chosen.Count + s.Unavailable.Count);
            })
            .ToList();

        var best = AppleMusicCatalog.FindBestMatch(candidates, artist, title);
        return best is null
            ? new TrackSelection(Array.Empty<LibraryTrack>(), Array.Empty<LibraryTrack>())
            : groups[(int)best.CollectionId];
    }

    // ---------- Reading back ----------

    public async Task<IReadOnlyList<PlaylistEntry>> ReadAlbumsAsync(string playlistName)
    {
        var output = await RunScriptAsync(ReadPlaylistScript, playlistName).ConfigureAwait(false);
        if (output.Trim() == NoPlaylist) return Array.Empty<PlaylistEntry>();

        var tracks = Rows(output).Select(f => new PlaylistTrack(
            Album: Field(f, 0),
            // Compilations leave "album artist" blank; the track artist is the only name there is.
            Artist: Field(f, 1).Length > 0 ? Field(f, 1) : Field(f, 2)));

        return PlaylistTracks.CollapseToAlbums(tracks);
    }

    // ---------- Music app plumbing ----------

    private static async Task<List<LibraryTrack>> SearchLibraryAsync(string term)
    {
        var output = await RunScriptAsync(SearchLibraryScript, term).ConfigureAwait(false);
        return Rows(output)
            .Select(f => new LibraryTrack(
                Album: Field(f, 0),
                // Compilations leave "album artist" blank; the track artist is the only name there is.
                AlbumArtist: Field(f, 1).Length > 0 ? Field(f, 1) : Field(f, 2),
                PersistentId: Field(f, 3),
                Disc: Number(Field(f, 4)),
                Number: Number(Field(f, 5)),
                Name: Field(f, 6),
                Playable: IsPlayable(Field(f, 7))))
            .Where(t => t.Album.Length > 0 && t.PersistentId.Length > 0)
            .ToList();
    }

    private static int Number(string field) => int.TryParse(field, out int n) ? n : 0;

    /// <summary>
    /// Whether Music will actually play this track. Anything other than a withdrawn track counts as
    /// playable — the statuses run to "subscription", "purchased", "matched", "uploaded" and more,
    /// and an unfamiliar one is far likelier to be a track that plays than one that doesn't.
    /// </summary>
    private static bool IsPlayable(string cloudStatus) =>
        !cloudStatus.Trim().Equals("no longer available", StringComparison.OrdinalIgnoreCase);

    private const int NoPlaylistResult = -1;

    /// <summary>
    /// Copies the chosen tracks into the playlist, and reports how many landed. The script re-runs
    /// the search rather than being handed track references (AppleScript can't take those across
    /// processes) and matches on persistent id, so a second album with the same name and artist
    /// can't be swept in alongside the intended one.
    /// </summary>
    private static async Task<int> DuplicateIntoPlaylistAsync(string playlistName, string term, List<LibraryTrack> tracks)
    {
        var args = new List<string> { playlistName, term };
        args.AddRange(tracks.Select(t => t.PersistentId));

        var output = (await RunScriptAsync(AddToPlaylistScript, args.ToArray()).ConfigureAwait(false)).Trim();
        if (output == NoPlaylist) return NoPlaylistResult;
        return int.TryParse(output, out int added) ? added : 0;
    }

    /// <summary>
    /// Runs one AppleScript via <c>osascript</c>, passing <paramref name="args"/> to its
    /// <c>on run argv</c> handler — never interpolated into the source, so an album title full of
    /// quotes can't break (or rewrite) the script.
    /// </summary>
    private static async Task<string> RunScriptAsync(string script, params string[] args)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/osascript")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-");           // read the script from stdin
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Couldn't start osascript.");

        await process.StandardInput.WriteAsync(script).ConfigureAwait(false);
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var cts = new System.Threading.CancellationTokenSource(ScriptTimeout);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new TimeoutException("The Music app didn't respond in time.");
        }

        if (process.ExitCode != 0)
        {
            var error = (await stderr.ConfigureAwait(false)).Trim();
            throw new InvalidOperationException(error.Length > 0 ? error : "osascript failed.");
        }
        return await stdout.ConfigureAwait(false);
    }

    private static IEnumerable<string[]> Rows(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
              .Select(line => line.TrimEnd('\r').Split(Sep));

    private static string Field(string[] fields, int index) =>
        index < fields.Length ? fields[index].Trim() : "";

    // ---------- The scripts ----------
    //
    // Each wraps itself in `with timeout` because Music's AppleEvents can take minutes against a
    // large cloud library and would otherwise fail with a -1712 timeout. Results come back as
    // tab-separated lines, which C# parses above.

    private const string SearchLibraryScript = """
        on run argv
          set searchTerm to item 1 of argv
          with timeout of 280 seconds
            tell application "Music"
              set rows to {}
              -- Searching the main library playlist covers the whole Apple Music catalog when
              -- Sync Library is on, not merely what's already been added to the library.
              repeat with t in (search library playlist 1 for searchTerm only albums)
                -- cloud status tells a playable track from one Apple Music has withdrawn; disc and
                -- track number say which song it is, so a withdrawn copy can be swapped for a live
                -- one off another edition of the same album.
                set end of rows to ((album of t) & tab & (album artist of t) & tab & ¬
                                    (artist of t) & tab & (persistent ID of t) & tab & ¬
                                    ((disc number of t) as text) & tab & ((track number of t) as text) & tab & ¬
                                    (name of t) & tab & ((cloud status of t) as text))
              end repeat
              set AppleScript's text item delimiters to linefeed
              return rows as text
            end tell
          end timeout
        end run
        """;

    private const string AddToPlaylistScript = """
        on run argv
          set playlistName to item 1 of argv
          set searchTerm to item 2 of argv
          set wanted to items 3 thru -1 of argv
          with timeout of 280 seconds
            tell application "Music"
              if not (exists user playlist playlistName) then return "!!NOPLAYLIST"
              set target to first user playlist whose name is playlistName
              set added to 0
              repeat with t in (search library playlist 1 for searchTerm only albums)
                if (persistent ID of t) is in wanted then
                  duplicate t to target
                  set added to added + 1
                end if
              end repeat
              return added as text
            end tell
          end timeout
        end run
        """;

    private const string ReadPlaylistScript = """
        on run argv
          set playlistName to item 1 of argv
          with timeout of 280 seconds
            tell application "Music"
              if not (exists user playlist playlistName) then return "!!NOPLAYLIST"
              set pl to first user playlist whose name is playlistName
              set rows to {}
              repeat with t in (every track of pl)
                set end of rows to ((album of t) & tab & (album artist of t) & tab & (artist of t))
              end repeat
              set AppleScript's text item delimiters to linefeed
              return rows as text
            end tell
          end timeout
        end run
        """;
}
