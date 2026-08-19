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

        var chosen = ChooseAlbum(hits, album.Artist, album.Title);
        if (chosen.Count == 0)
            return new PlaylistOpResult(false, $"Not found in Music: {album.Title}");

        try
        {
            var added = await DuplicateIntoPlaylistAsync(playlistName, album.Title, chosen);
            if (added == NoPlaylistResult)
                return new PlaylistOpResult(false, $"No Music playlist named “{playlistName}” — create it in Music first.");
            if (added == 0)
                return new PlaylistOpResult(false, $"Music didn't add any tracks for {album.Title}.");

            return new PlaylistOpResult(true, $"Added {added} track{(added == 1 ? "" : "s")} to {playlistName}");
        }
        catch (Exception ex)
        {
            return new PlaylistOpResult(false, $"Couldn't add: {ex.Message}");
        }
    }

    /// <summary>
    /// Picks which of the search hits actually is the wanted album and returns its tracks.
    /// <para>
    /// The hits are grouped into albums and handed to the very same
    /// <see cref="AppleMusicCatalog.FindBestMatch"/> the iPhone uses, so both apps resolve an album
    /// identically — including preferring the original release over a padded "(Deluxe Edition)"
    /// reissue sitting beside it in the catalog. The group's index rides along in the record's id
    /// field purely so the winner can be mapped back to its tracks.
    /// </para>
    /// </summary>
    private static List<LibraryTrack> ChooseAlbum(List<LibraryTrack> hits, string artist, string title)
    {
        var groups = hits
            .GroupBy(t => $"{NumberedList.Normalize(t.Album)}|{NumberedList.Normalize(t.AlbumArtist)}")
            .Select(g => g.ToList())
            .ToList();

        var candidates = groups
            .Select((g, i) => new AppleMusicAlbum(i, g[0].Album, g[0].AlbumArtist, g.Count))
            .ToList();

        var best = AppleMusicCatalog.FindBestMatch(candidates, artist, title);
        return best is null ? new List<LibraryTrack>() : groups[(int)best.CollectionId];
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

    /// <summary>One track as the Music app reported it.</summary>
    private sealed record LibraryTrack(string Album, string AlbumArtist, string Artist, string PersistentId);

    private static async Task<List<LibraryTrack>> SearchLibraryAsync(string term)
    {
        var output = await RunScriptAsync(SearchLibraryScript, term).ConfigureAwait(false);
        return Rows(output)
            .Select(f => new LibraryTrack(
                Album: Field(f, 0),
                AlbumArtist: Field(f, 1).Length > 0 ? Field(f, 1) : Field(f, 2),
                Artist: Field(f, 2),
                PersistentId: Field(f, 3)))
            .Where(t => t.Album.Length > 0 && t.PersistentId.Length > 0)
            .ToList();
    }

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
                set end of rows to ((album of t) & tab & (album artist of t) & tab & ¬
                                    (artist of t) & tab & (persistent ID of t))
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
