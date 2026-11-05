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
/// Which album to add is decided in the Apple Music <em>catalog</em>, never the library: the
/// iTunes Search API names the release (original, else a remaster, never a deluxe edition when
/// there's anything leaner — see <see cref="AppleMusicCatalog.FindBestMatch"/>). The library only
/// matters as the staging area AppleScript can reach: a playlist can only be handed library tracks,
/// and AppleScript has no verb for adding a catalog album. So when the library doesn't already hold
/// the whole release, the writer opens its page in Music and clicks Music's own "Add to Library"
/// button over UI scripting (see <see cref="EnsureInLibraryAsync"/>), waits for the tracks to land,
/// and copies exactly that release's tracks into the playlist. A stray song or two of the album
/// already sitting in the library can't stand in for it any more.
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
        var copy = await EnsureInLibraryAsync(album).ConfigureAwait(false);
        if (copy.Problem is not null) return new PlaylistOpResult(false, copy.Problem, copy.OpenUrl);

        var selection = copy.Selection;
        if (selection.Chosen.Count == 0)
            return new PlaylistOpResult(false, $"Apple Music no longer has a playable copy of {album.Title}.");

        try
        {
            var added = await DuplicateIntoPlaylistAsync(playlistName, copy.Term, selection.Chosen.ToList());
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

            return new PlaylistOpResult(true,
                $"Added {copy.ReleaseName} ({added} track{(added == 1 ? "" : "s")}) to {playlistName}{missing}");
        }
        catch (Exception ex)
        {
            return new PlaylistOpResult(false, $"Couldn't add: {ex.Message}");
        }
    }

    /// <summary>
    /// The library's copy of one catalog release, ready to act on — or why there isn't one.
    /// </summary>
    /// <param name="Term">What to search the library for to find these tracks again: the release's name.</param>
    /// <param name="OpenUrl">The release's page in Music, for a problem the user can fix there by hand.</param>
    private sealed record LibraryCopy(
        TrackSelection Selection, string Term, string ReleaseName, string? Problem = null, string? OpenUrl = null);

    /// <summary>The last album resolved, so the favorites backfill's favorite-then-add pair resolves once.</summary>
    private (string Key, DateTime At, LibraryCopy Copy)? _lastCopy;

    /// <summary>How long to wait for an added album's tracks to appear in the library.</summary>
    private static readonly TimeSpan LibraryArrivalTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Makes sure the library holds the whole of the album's catalog release, adding it through
    /// Music if not, and returns that release's library tracks.
    /// <para>
    /// The release is chosen in the catalog. The library is then read for tracks filed under exactly
    /// that release's name and artist — so a single song of the album added months ago is seen for
    /// what it is, one track of eleven, and the album is added anyway. Music fills in the rest
    /// rather than doubling the track that was already there.
    /// </para>
    /// </summary>
    private async Task<LibraryCopy> EnsureInLibraryAsync(PlaylistEntry album)
    {
        string key = $"{NumberedList.Normalize(album.Title)}|{NumberedList.Normalize(album.Artist)}";
        if (_lastCopy is { } last && last.Key == key && DateTime.UtcNow - last.At < TimeSpan.FromMinutes(2)
            && last.Copy.Problem is null)
            return last.Copy;

        var copy = await ResolveAsync(album).ConfigureAwait(false);
        _lastCopy = (key, DateTime.UtcNow, copy);
        return copy;
    }

    private static async Task<LibraryCopy> ResolveAsync(PlaylistEntry album)
    {
        var none = new TrackSelection(Array.Empty<LibraryTrack>(), Array.Empty<LibraryTrack>());

        AppleMusicAlbum? release;
        try
        {
            release = await AppleMusicCatalog.FindAlbumAsync(album.Artist, album.Title).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new LibraryCopy(none, album.Title, album.Title, $"Couldn't look {album.Title} up in Apple Music: {ex.Message}");
        }
        if (release is null)
            return new LibraryCopy(none, album.Title, album.Title, $"Apple Music doesn't seem to have {album.Title}.");

        string name = release.CollectionName;
        string url = AppleMusicCatalog.MusicAppUrl(release);
        LibraryCopy Copy(TrackSelection sel, string? problem = null) =>
            new(sel, name, name, problem, problem is null ? null : url);

        try
        {
            var selection = await ReadLibraryCopyAsync(release).ConfigureAwait(false);
            if (Count(selection) >= release.TrackCount) return Copy(selection);

            string? addProblem = await AddToLibraryAsync(release, url).ConfigureAwait(false);
            // Already in the library: nothing new is on its way, so waiting for more tracks would
            // only sit out the timeout (a catalog count can include songs never released here).
            if (addProblem == AlreadyInLibrary && Count(selection) > 0) return Copy(selection);
            if (addProblem is not null && addProblem != AlreadyInLibrary)
                return Count(selection) > 0 ? Copy(selection) : Copy(selection, addProblem);

            selection = await WaitForLibraryCopyAsync(release, Count(selection)).ConfigureAwait(false);
            return Count(selection) > 0
                ? Copy(selection)
                : Copy(selection, $"Music didn't put {name} in your library in time — try pushing again.");
        }
        catch (Exception ex)
        {
            return Copy(none, $"Music app lookup failed: {ex.Message}");
        }
    }

    private static int Count(TrackSelection s) => s.Chosen.Count + s.Unavailable.Count;

    /// <summary>
    /// The library's tracks of exactly this release: filed under its name, by its artist, reduced
    /// to one copy per song (see <see cref="PlaylistTracks.PreferPlayable"/>).
    /// </summary>
    private static async Task<TrackSelection> ReadLibraryCopyAsync(AppleMusicAlbum release)
    {
        var hits = await RunSearchAsync(release.CollectionName).ConfigureAwait(false);
        return PlaylistTracks.PreferPlayable(hits.Where(t =>
            NumberedList.Matches(t.Album, release.CollectionName)
            && (NumberedList.Matches(t.AlbumArtist, release.ArtistName)
                || DiscogsApiClient.ArtistsOverlap(t.AlbumArtist, release.ArtistName))));
    }

    /// <summary>
    /// Polls the library until the release's tracks have arrived: all of them, or a count that has
    /// stopped growing (a withdrawn song never arrives), or the timeout.
    /// </summary>
    private static async Task<TrackSelection> WaitForLibraryCopyAsync(AppleMusicAlbum release, int before)
    {
        var deadline = DateTime.UtcNow + LibraryArrivalTimeout;
        var selection = new TrackSelection(Array.Empty<LibraryTrack>(), Array.Empty<LibraryTrack>());
        int lastCount = -1, steady = 0;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            selection = await ReadLibraryCopyAsync(release).ConfigureAwait(false);
            int count = Count(selection);
            if (count >= release.TrackCount) break;

            steady = count == lastCount && count > before ? steady + 1 : 0;
            if (steady >= 3) break;
            lastCount = count;
        }
        return selection;
    }

    /// <summary>What <see cref="AddToLibraryAsync"/> returns when the page shows the album already added.</summary>
    private const string AlreadyInLibrary = "!!ALREADY";

    /// <summary>How long to wait for Music to show the album's page after opening its link.</summary>
    private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Opens the release's page in Music and presses "Add to Library" — the one step AppleScript's
    /// own dictionary can't take. Null when it was pressed, <see cref="AlreadyInLibrary"/> when the
    /// page says it's already added, else what stopped it, worded for the user.
    /// <para>
    /// The button is only pressed once the page on screen is this release's: until Music has
    /// navigated, the toolbar still belongs to the previous album, and pressing it there would add
    /// the wrong record. UI scripting needs the Accessibility permission, and can only see Music's
    /// window when it's on the current desktop — both are spelled out when they're the problem.
    /// </para>
    /// </summary>
    private static async Task<string?> AddToLibraryAsync(AppleMusicAlbum release, string url)
    {
        try
        {
            using (var open = Process.Start(new ProcessStartInfo("/usr/bin/open", new[] { url }) { UseShellExecute = false }))
                if (open is not null) await open.WaitForExitAsync().ConfigureAwait(false);

            var deadline = DateTime.UtcNow + PageTimeout;
            bool sawWindow = false, nudged = false;
            string seen = "";

            while (DateTime.UtcNow < deadline)
            {
                var state = (await RunScriptAsync(ReadMusicPageScript).ConfigureAwait(false)).TrimEnd('\n', '\r');
                if (state == NoWindow)
                {
                    // `open` usually brings Music forward; if it didn't, ask once more.
                    if (!nudged) { nudged = true; await RunScriptAsync(ActivateMusicScript).ConfigureAwait(false); }
                }
                else
                {
                    sawWindow = true;
                    var parts = state.Split(Sep);
                    string heading = parts[0];
                    var buttons = parts.Length > 1 ? parts[1].Split(',') : Array.Empty<string>();
                    seen = heading;

                    if (IsPageFor(heading, release.CollectionName))
                    {
                        if (buttons.Contains(AddToLibraryButton))
                        {
                            var clicked = (await RunScriptAsync(ClickAddToLibraryScript, heading).ConfigureAwait(false)).Trim();
                            if (clicked == "clicked") return null;
                        }
                        else if (buttons.Contains("Download") || buttons.Contains("Remove from Library"))
                        {
                            return AlreadyInLibrary; // under whatever name the library files it
                        }
                    }
                }
                await Task.Delay(500).ConfigureAwait(false);
            }

            return sawWindow
                ? $"Music's page for {release.CollectionName} didn't offer “Add to Library”" + (seen.Length > 0 ? $" (it showed “{seen}”)." : ".")
                : "Couldn't see the Music window. Keep Music open on the same desktop as this app — not in full screen — and push again.";
        }
        catch (Exception ex) when (ex.Message.Contains("assistive access", StringComparison.OrdinalIgnoreCase)
                                   || ex.Message.Contains("-1728") || ex.Message.Contains("-25211"))
        {
            return "Allow “1001 Albums Helper” in System Settings ▸ Privacy & Security ▸ Accessibility, so it can press Add in Music.";
        }
    }

    /// <summary>Whether the album page on screen is this release's — exact name, else same bare title.</summary>
    internal static bool IsPageFor(string heading, string releaseName) =>
        heading.Length > 0
        && (NumberedList.Matches(heading, releaseName)
            || DiscogsApiClient.SameTitle(AppleMusicCatalog.SplitEdition(heading).Bare,
                                          AppleMusicCatalog.SplitEdition(releaseName).Bare));


    // ---------- Removing ----------

    /// <summary>Music deletes a track from a user playlist without touching the library, so yes.</summary>
    public bool CanRemove => true;

    public async Task<PlaylistOpResult> RemoveAlbumAsync(string playlistName, PlaylistEntry album)
    {
        try
        {
            var output = await RunScriptAsync(ReadPlaylistTracksScript, playlistName).ConfigureAwait(false);
            if (output.Trim() == NoPlaylist)
                return new PlaylistOpResult(false, $"No Music playlist named “{playlistName}”.");

            var ids = Rows(output)
                .Where(f => IsSameAlbum(Field(f, 0), Field(f, 1).Length > 0 ? Field(f, 1) : Field(f, 2), album))
                .Select(f => Field(f, 3))
                .Where(id => id.Length > 0)
                .ToList();
            if (ids.Count == 0) return new PlaylistOpResult(true, $"{album.Title} wasn't in {playlistName}.");

            var args = new List<string> { playlistName };
            args.AddRange(ids);
            var removed = (await RunScriptAsync(RemoveFromPlaylistScript, args.ToArray()).ConfigureAwait(false)).Trim();
            return int.TryParse(removed, out int n) && n > 0
                ? new PlaylistOpResult(true, $"Took {album.Title} out of {playlistName} ({n} track{(n == 1 ? "" : "s")}).")
                : new PlaylistOpResult(false, $"Music didn't remove {album.Title} from {playlistName}.");
        }
        catch (Exception ex)
        {
            return new PlaylistOpResult(false, $"Couldn't remove {album.Title}: {ex.Message}");
        }
    }

    /// <summary>
    /// Whether a playlist track belongs to this album. Stricter than the pull-down's prefix rule on
    /// purpose — this decides what gets deleted, and "Tical" must not take "Tical 2000: Judgement
    /// Day" with it — so the titles have to be the same once edition tags are set aside on both
    /// sides ("Haunted Dancehall" and "Haunted Dancehall (Remastered)" are).
    /// </summary>
    internal static bool IsSameAlbum(string trackAlbum, string trackArtist, PlaylistEntry album) =>
        AppleMusicCatalog.SameRelease(trackAlbum, album.Title)
        && (NumberedList.Matches(trackArtist, album.Artist) || DiscogsApiClient.ArtistsOverlap(trackArtist, album.Artist));

    // ---------- Favorites ----------

    /// <summary>The Music app's <c>album favorited</c> is read/write over AppleScript, so yes.</summary>
    public bool CanFavorite => true;

    /// <summary>
    /// Every favorited album in the library. Read with three <c>whose album favorited is true</c>
    /// passes rather than by walking the library and asking each track: a bulk get is one AppleEvent
    /// returning a whole column, where the walk would be tens of thousands of round trips against a
    /// library this size. The three columns line up row for row, and are folded into albums by the
    /// same <see cref="PlaylistTracks.CollapseToAlbums"/> a playlist read uses.
    /// </summary>
    public async Task<IReadOnlyList<PlaylistEntry>> ReadFavoritedAlbumsAsync()
    {
        var output = await RunScriptAsync(ReadFavoritedScript).ConfigureAwait(false);

        var tracks = Rows(output).Select(f => new PlaylistTrack(
            Album: Field(f, 0),
            // Compilations leave "album artist" blank; the track artist is the only name there is.
            Artist: Field(f, 1).Length > 0 ? Field(f, 1) : Field(f, 2)));

        return PlaylistTracks.CollapseToAlbums(tracks);
    }

    /// <summary>
    /// Marks an album as a favorite, choosing which album that is exactly the way
    /// <see cref="AddAlbumAsync"/> does — so the record that gets the heart is the record that
    /// would have gone into the playlist, deluxe-edition tie-breaks and all.
    /// </summary>
    public async Task<PlaylistOpResult> FavoriteAlbumAsync(PlaylistEntry album)
    {
        var copy = await EnsureInLibraryAsync(album).ConfigureAwait(false);
        if (copy.Problem is not null) return new PlaylistOpResult(false, copy.Problem, copy.OpenUrl);

        // Unlike adding, a withdrawn track is no obstacle: it still carries the album's favorite
        // flag, so an album Apple Music no longer streams can still be marked.
        var marked = copy.Selection.Chosen.Concat(copy.Selection.Unavailable).ToList();
        if (marked.Count == 0)
            return new PlaylistOpResult(false, $"Music has no tracks of {album.Title} to favorite.");

        try
        {
            var args = new List<string> { copy.Term };
            args.AddRange(marked.Select(t => t.PersistentId));
            var output = (await RunScriptAsync(FavoriteAlbumScript, args.ToArray()).ConfigureAwait(false)).Trim();

            return int.TryParse(output, out int touched) && touched > 0
                ? new PlaylistOpResult(true, $"Favorited {album.Title}")
                : new PlaylistOpResult(false, $"Music didn't mark {album.Title} as a favorite.");
        }
        catch (Exception ex)
        {
            return new PlaylistOpResult(false, $"Couldn't favorite: {ex.Message}");
        }
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

    private static async Task<List<LibraryTrack>> RunSearchAsync(string term)
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
              -- This reaches the library only — albums already added — not the Apple Music catalog,
              -- which is why EnsureInLibraryAsync adds a release before it's searched for here.
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

    /// <summary>Sentinel the page reader returns when Music has no window it can see.</summary>
    private const string NoWindow = "!!NOWINDOW";

    /// <summary>The accessibility description Music gives its toolbar ＋ on an album page.</summary>
    private const string AddToLibraryButton = "Add to Library";

    private const string ActivateMusicScript = """
        tell application "Music"
          reopen
          activate
        end tell
        """;

    // Reads the album page Music is showing: its title (the first text in the content pane's list)
    // and the descriptions of the toolbar's buttons — "Add to Library" before the album is added,
    // "Download" after. Addressed by path rather than by `entire contents`, which takes ~15s here.
    private const string ReadMusicPageScript = """
        with timeout of 20 seconds
          tell application "System Events" to tell process "Music"
            if (count windows) = 0 then return "!!NOWINDOW"
            set heading to ""
            try
              set sg to splitter group 1 of window 1
              set lst to list 1 of list 1 of scroll area (count scroll areas of sg) of sg
              repeat with u in (UI elements of lst)
                try
                  set heading to value of static text 1 of u
                  exit repeat
                end try
              end repeat
            end try
            set btns to {}
            try
              set btns to description of every button of toolbar 1 of window 1
            end try
            set AppleScript's text item delimiters to ","
            return heading & tab & (btns as text)
          end tell
        end timeout
        """;

    // Presses "Add to Library" — but only if the page is still the one the caller just read.
    private const string ClickAddToLibraryScript = """
        on run argv
          set wanted to item 1 of argv
          with timeout of 20 seconds
            tell application "System Events" to tell process "Music"
              set sg to splitter group 1 of window 1
              set lst to list 1 of list 1 of scroll area (count scroll areas of sg) of sg
              set heading to ""
              repeat with u in (UI elements of lst)
                try
                  set heading to value of static text 1 of u
                  exit repeat
                end try
              end repeat
              if heading is not wanted then return "moved"
              click (first button of toolbar 1 of window 1 whose description is "Add to Library")
              return "clicked"
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

    private const string ReadFavoritedScript = """
        on run argv
          with timeout of 280 seconds
            tell application "Music"
              set src to library playlist 1
              -- Three bulk gets, not a walk: each is a single AppleEvent returning one whole
              -- column of the filtered set, and they come back row-aligned with each other.
              set albumNames to (album of every track of src whose album favorited is true)
              set albumArtists to (album artist of every track of src whose album favorited is true)
              set trackArtists to (artist of every track of src whose album favorited is true)
              set n to count of albumNames
              if (count of albumArtists) < n then set n to count of albumArtists
              if (count of trackArtists) < n then set n to count of trackArtists
              set rows to {}
              repeat with i from 1 to n
                set end of rows to ((item i of albumNames) & tab & ¬
                                    (item i of albumArtists) & tab & (item i of trackArtists))
              end repeat
              set AppleScript's text item delimiters to linefeed
              return rows as text
            end tell
          end timeout
        end run
        """;

    private const string FavoriteAlbumScript = """
        on run argv
          set searchTerm to item 1 of argv
          set wanted to items 2 thru -1 of argv
          with timeout of 280 seconds
            tell application "Music"
              set touched to 0
              -- Setting it on any one track of the album favorites the whole album; every track is
              -- set anyway so that a copy Music files separately can't be left behind.
              repeat with t in (search library playlist 1 for searchTerm only albums)
                if (persistent ID of t) is in wanted then
                  set album favorited of t to true
                  set touched to touched + 1
                end if
              end repeat
              return touched as text
            end tell
          end timeout
        end run
        """;

    private const string ReadPlaylistTracksScript = """
        on run argv
          set playlistName to item 1 of argv
          with timeout of 280 seconds
            tell application "Music"
              if not (exists user playlist playlistName) then return "!!NOPLAYLIST"
              set pl to first user playlist whose name is playlistName
              set rows to {}
              repeat with t in (every track of pl)
                set end of rows to ((album of t) & tab & (album artist of t) & tab & (artist of t) & tab & (persistent ID of t))
              end repeat
              set AppleScript's text item delimiters to linefeed
              return rows as text
            end tell
          end timeout
        end run
        """;

    // Deletes tracks from one user playlist by persistent id. Deleting a playlist's track removes it
    // from that playlist only — the library copy stays.
    private const string RemoveFromPlaylistScript = """
        on run argv
          set playlistName to item 1 of argv
          set wanted to items 2 thru -1 of argv
          with timeout of 280 seconds
            tell application "Music"
              set pl to first user playlist whose name is playlistName
              -- "gone", not "removed": that word is taken in Music's dictionary.
              set gone to 0
              repeat with pid in wanted
                set hits to (every track of pl whose persistent ID is (pid as text))
                repeat with t in hits
                  delete t
                  set gone to gone + 1
                end repeat
              end repeat
              return gone as text
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
