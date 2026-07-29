using System.Diagnostics;

namespace _1001AlbumHelper;

/// <summary>One album currently in a real Apple Music playlist, as read via AppleScript.</summary>
public sealed record MusicAppAlbum(string Title, string Artist, int TrackCount);

/// <summary>
/// macOS-only: talks to Music.app directly via AppleScript, driving the same "Music" scripting
/// dictionary Script Editor sees. Unlike the iPhone's MediaPlayer-based access (add-only — see
/// <see cref="MediaPlayerPlaylistWriter"/> and PROJECT.md §5), Music.app's dictionary can actually
/// remove tracks from a playlist — that's the whole point of this class. If the Mac and phone share
/// an Apple Music library (iCloud Music Library / Sync Library turned on), a removal made here syncs
/// to the phone too, typically within a few minutes.
/// <para>
/// Removing a track from a playlist only unlists it from that playlist — the track stays in the
/// library untouched, so a wrong click here is cheap to undo (just re-add it).
/// </para>
/// </summary>
public static class MusicAppPlaylistWriter
{
    public static bool IsAvailable => OperatingSystem.IsMacOS();

    /// <summary>Every album currently in the named playlist, in first-seen order, with each album's track count.</summary>
    public static async Task<IReadOnlyList<MusicAppAlbum>> ReadAlbumsAsync(string playlistName, CancellationToken ct = default)
    {
        // One "album<TAB>artist" line per track, rather than returning an AppleScript list — parsing
        // AppleScript's own list-literal syntax back out is far more fragile than a plain delimiter.
        string script = $$"""
            tell application "Music"
                if not (exists playlist "{{Escape(playlistName)}}") then return "NO_PLAYLIST"
                set out to ""
                repeat with t in (every track of playlist "{{Escape(playlistName)}}")
                    set out to out & (album of t) & "\t" & (artist of t) & "\n"
                end repeat
                return out
            end tell
            """;

        var (exitCode, stdout, stderr) = await RunAppleScriptAsync(script, ct).ConfigureAwait(false);
        if (exitCode != 0) throw new InvalidOperationException($"Music.app read failed: {stderr}");
        if (stdout == "NO_PLAYLIST")
            throw new InvalidOperationException($"No Apple Music playlist named \"{playlistName}\".");

        var order = new List<string>();
        var byKey = new Dictionary<string, (string Title, string Artist, int Count)>();
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            string title = parts[0].Trim(), artist = parts[1].Trim();
            if (title.Length == 0) continue;

            string key = $"{NumberedList.Normalize(title)}|{NumberedList.Normalize(artist)}";
            if (byKey.TryGetValue(key, out var existing))
                byKey[key] = (existing.Title, existing.Artist, existing.Count + 1);
            else
            {
                byKey[key] = (title, artist, 1);
                order.Add(key);
            }
        }
        return order.Select(k =>
        {
            var (title, artist, count) = byKey[k];
            return new MusicAppAlbum(title, artist, count);
        }).ToList();
    }

    /// <summary>Removes every track matching this album from the named playlist. The library itself is untouched.</summary>
    public static async Task<(bool Ok, string Message)> RemoveAlbumAsync(
        string playlistName, string title, string artist, CancellationToken ct = default)
    {
        string script = $$"""
            tell application "Music"
                if not (exists playlist "{{Escape(playlistName)}}") then return "NO_PLAYLIST"
                set matches to (every track of playlist "{{Escape(playlistName)}}" whose album is "{{Escape(title)}}" and artist is "{{Escape(artist)}}")
                set n to count of matches
                if n is 0 then return "NOT_FOUND"
                delete matches
                return "OK:" & n
            end tell
            """;

        var (exitCode, stdout, stderr) = await RunAppleScriptAsync(script, ct).ConfigureAwait(false);
        if (exitCode != 0) return (false, $"Music.app error: {stderr}");
        if (stdout == "NO_PLAYLIST") return (false, $"No Apple Music playlist named \"{playlistName}\".");
        if (stdout == "NOT_FOUND") return (false, $"\"{title}\" isn't in \"{playlistName}\" anymore.");

        int count = int.TryParse(stdout.AsSpan(stdout.IndexOf(':') + 1), out int n) ? n : 0;
        return (true, $"Removed {count} track{(count == 1 ? "" : "s")} of \"{title}\" from \"{playlistName}\".");
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunAppleScriptAsync(string script, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("osascript")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start osascript.");
        await process.StandardInput.WriteAsync(script.AsMemory(), ct).ConfigureAwait(false);
        process.StandardInput.Close();

        string stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        string stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return (process.ExitCode, stdout.Trim(), stderr.Trim());
    }

    // AppleScript double-quoted string literals: backslash and quote both need escaping.
    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
