using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace _1001AlbumHelper;

/// <summary>
/// Headless check of <see cref="MusicAppPlaylistWriter"/> — the Mac's Apple Music path — in the
/// same self-cleaning spirit as <see cref="RestSheetsClientDiagnostic"/>: it makes its own scratch
/// playlist in the Music app, pushes real albums into it, reads them back, and deletes it again, so
/// PLAYLIST1/PLAYLIST2 are never touched.
///
/// <para>Run it with <c>dotnet run --project 1001AlbumHelper.Desktop -- musictest</c>.</para>
/// </summary>
public static class MusicAppDiagnostic
{
    private const string ScratchPlaylist = "ZZ_AlbumHelper_Scratch";

    public static async Task RunAsync()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Console.WriteLine("✗ The Music app path is macOS-only.");
            return;
        }

        // A mix on purpose: a famous album, two that weren't in the library when this was written
        // (so they only arrive if the catalog add — opening the page and pressing Add to Library —
        // works; once added they stay, and later runs exercise the already-there path), one Apple
        // Music lists twice with tracks withdrawn on one of the two, and one that shouldn't exist.
        // Pushing needs Music on the same desktop as the terminal, and Accessibility for it.
        var albums = new[]
        {
            new PlaylistEntry("Kind of Blue", "Miles Davis", "1959"),
            new PlaylistEntry("Haunted Dancehall", "The Sabres Of Paradise", "1994"),
            new PlaylistEntry("Ill Communication", "Beastie Boys", "1994"),
            new PlaylistEntry("Tical 2000: Judgement Day", "Method Man", "1998"),
            new PlaylistEntry("Definitely Not A Real Album 12345", "Nobody At All", "1999"),
        };

        // Tical 2000 is the reason PlaylistTracks.PreferPlayable exists: the search returns 56 rows
        // for a 28-track album, five of them withdrawn copies whose twins play fine. Adding every
        // row would put the album in twice over, five of those tracks dead. The number here is the
        // check that we now add one playable copy of each song — a count anywhere near 56 means the
        // per-song choice has stopped working.
        const string DoubleListed = "Tical 2000: Judgement Day";
        const int DoubleListedCeiling = 40;

        Console.WriteLine($"▶ Creating the scratch playlist “{ScratchPlaylist}”…");
        if (!await ScratchAsync(create: true)) { Console.WriteLine("✗ Couldn't create it."); return; }

        try
        {
            var writer = new MusicAppPlaylistWriter();

            Console.WriteLine("▶ Pushing albums…");
            foreach (var album in albums)
            {
                var result = await writer.AddAlbumAsync(ScratchPlaylist, album);
                Console.WriteLine($"   {(result.Ok ? "✓" : "✗")} {album.Title} — {album.Artist}: {result.Message}");
            }

            Console.WriteLine("▶ Reading the playlist back…");
            var read = await writer.ReadAlbumsAsync(ScratchPlaylist);
            foreach (var entry in read)
                Console.WriteLine($"   • {entry.Display}");

            // The four real albums should have gone in and come back; the made-up one shouldn't.
            // Compared loosely, because the catalog's name for an album is often the reissue's —
            // "Haunted Dancehall" comes back as "Haunted Dancehall (Remastered)", still a match.
            var real = albums.Take(albums.Length - 1).ToList();
            bool ok = read.Count == real.Count
                      && real.All(a => read.Any(r => DiscogsApiClient.TitlesLineUp(r.Title, a.Title)));
            Console.WriteLine(ok
                ? $"✓ Round trip matches: the {real.Count} real albums went in and came back."
                : $"✗ Expected the {real.Count} real albums back, got {read.Count}.");

            var doubled = read.FirstOrDefault(r => DiscogsApiClient.TitlesLineUp(r.Title, DoubleListed));
            if (doubled is null)
                Console.WriteLine($"✗ {DoubleListed} didn't come back at all.");
            else
                Console.WriteLine(doubled.TrackCount <= DoubleListedCeiling
                    ? $"✓ {DoubleListed} came back as {doubled.TrackCount} tracks — one playable copy of each song."
                    : $"✗ {DoubleListed} came back as {doubled.TrackCount} tracks — both listings went in.");

            Console.WriteLine("▶ Checking the pull-down sync against what Music actually returned…");
            await CheckPullDownAsync(writer);

            // Removing takes the album out of the playlist and leaves the library alone. Kind of Blue
            // is in the library to start with, so a library count of zero afterwards would mean the
            // delete reached past the playlist — exactly what it must never do.
            Console.WriteLine("▶ Removing Kind of Blue from the playlist only…");
            var removal = await writer.RemoveAlbumAsync(ScratchPlaylist, albums[0]);
            Console.WriteLine($"   {(removal.Ok ? "✓" : "✗")} {removal.Message}");
            var afterRemoval = await writer.ReadAlbumsAsync(ScratchPlaylist);
            Report("gone from the playlist", afterRemoval.All(r => !DiscogsApiClient.TitlesLineUp(r.Title, albums[0].Title)));
            Report("the other albums still there", afterRemoval.Count == real.Count - 1);
            Report("still in the library", await LibraryTrackCountAsync("Kind of Blue", "Miles Davis") > 0);

            Console.WriteLine("▶ Checking a playlist that doesn't exist is reported, not crashed…");
            var missing = await writer.AddAlbumAsync("ZZ_AlbumHelper_NoSuchPlaylist", albums[0]);
            Console.WriteLine($"   {(missing.Ok ? "✗ unexpectedly succeeded" : "✓ " + missing.Message)}");
        }
        finally
        {
            Console.WriteLine($"▶ Deleting “{ScratchPlaylist}”…");
            Console.WriteLine(await ScratchAsync(create: false) ? "✓ Cleaned up." : "⚠️  Couldn't delete it — remove it in Music by hand.");
        }
    }

    /// <summary>
    /// Exercises <see cref="PlaylistStore.SyncFromAppleMusic"/> on real Music-app data: seeds a
    /// working list with one album that is in the playlist and one that isn't, pulls down, and
    /// checks the stale one was dropped, the real one kept with its year, and a checklist entry
    /// left alone. Uses playlist id 99 — never 1 or 2 — and deletes its file afterwards.
    /// </summary>
    private static async Task CheckPullDownAsync(MusicAppPlaylistWriter writer)
    {
        const int ScratchId = 99;
        var path = Path.Combine(PlaylistStore.DataDir, $"playlist{ScratchId}.json");
        File.Delete(path);

        try
        {
            var store = PlaylistStore.Open(ScratchId);
            store.Add("Kind of Blue", "Miles Davis", "1959");     // really in the scratch playlist
            store.Add("Not In The Playlist", "Nobody", "2001");   // stale — should be dropped

            // One album the user has taken off the list but Apple Music still has: it must stay on
            // the checklist rather than being pulled back in as active.
            store.Add("Haunted Dancehall", "The Sabres Of Paradise", "1994");
            store.MarkInAppleMusic(store.Active.First(e => e.Title == "Haunted Dancehall"));
            store.RequestRemoval(store.Active.First(e => e.Title == "Haunted Dancehall"));

            var albums = await writer.ReadAlbumsAsync(ScratchPlaylist);
            var sync = store.SyncFromAppleMusic(albums);

            Console.WriteLine($"   added={sync.Added} removed={sync.Removed} cleared={sync.ClearedFromChecklist}");
            foreach (var e in store.Active) Console.WriteLine($"   • active: {e.Display}");
            foreach (var e in store.ToRemove) Console.WriteLine($"   • checklist: {e.Display}");

            var kept = store.Active.FirstOrDefault(e => e.Title == "Kind of Blue");
            Report("stale album dropped", store.Active.All(e => e.Title != "Not In The Playlist"));
            Report("year preserved on the kept album", kept?.Year == "1959");
            Report("checklist album not resurrected", store.Active.All(e => !DiscogsApiClient.TitlesLineUp(e.Title, "Haunted Dancehall")));
            Report("checklist album still listed", store.ToRemove.Any(e => e.Title == "Haunted Dancehall"));
            Report("the third album came in fresh", store.Active.Any(e => e.Title.Contains("Ill Communication")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void Report(string what, bool ok) => Console.WriteLine($"   {(ok ? "✓" : "✗")} {what}");

    /// <summary>How many library tracks Music files under this album and artist.</summary>
    private static async Task<int> LibraryTrackCountAsync(string album, string artist)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/osascript")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[]
                 {
                     "-e", "on run argv",
                     "-e", "tell application \"Music\" to return count (every track of library playlist 1 whose album is (item 1 of argv) and artist is (item 2 of argv))",
                     "-e", "end run", album, artist,
                 })
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo);
        if (process is null) return 0;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return int.TryParse(output.Trim(), out int n) ? n : 0;
    }

    /// <summary>Creates or deletes the scratch playlist. Kept here, not in the writer, which has no business making playlists.</summary>
    private static async Task<bool> ScratchAsync(bool create)
    {
        // $$ so AppleScript's own braces in `with properties {name:…}` stay literal.
        string body = create
            ? $$"""
               if exists user playlist "{{ScratchPlaylist}}" then delete (first user playlist whose name is "{{ScratchPlaylist}}")
               make new user playlist with properties {name:"{{ScratchPlaylist}}"}
               """
            : $$"""
               if exists user playlist "{{ScratchPlaylist}}" then delete (first user playlist whose name is "{{ScratchPlaylist}}")
               """;

        var startInfo = new ProcessStartInfo("/usr/bin/osascript")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-");

        using var process = Process.Start(startInfo);
        if (process is null) return false;

        await process.StandardInput.WriteAsync($"""
            with timeout of 280 seconds
              tell application "Music"
                {body}
              end tell
            end timeout
            """);
        process.StandardInput.Close();

        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0) Console.WriteLine($"   {error.Trim()}");
        return process.ExitCode == 0;
    }
}
