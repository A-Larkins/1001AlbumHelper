using System;
using System.Linq;
using System.Threading.Tasks;

namespace _1001AlbumHelper;

/// <summary>
/// A dry run of what pulling PLAYLIST2 down would put on the potentials shortlist. It's in the
/// spirit of the other headless checks, with one difference worth being clear about: this one
/// writes <em>nothing at all</em> — not the working list, not the shortlist file, not the Potentials
/// sheet — so it can be run before the real pull to see what that pull would decide.
///
/// <para>Run it with <c>dotnet run --project 1001AlbumHelper.Desktop -- intaketest</c>.</para>
/// </summary>
public static class ShortlistIntakeDiagnostic
{
    private const string PlaylistName = "PLAYLIST2";

    public static async Task RunAsync()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Console.WriteLine("✗ Reading a Music app playlist is macOS-only.");
            return;
        }

        Console.WriteLine($"▶ Reading “{PlaylistName}” from the Music app…");
        var albums = await new MusicAppPlaylistWriter().ReadAlbumsAsync(PlaylistName);
        Console.WriteLine($"   {albums.Count} album{(albums.Count == 1 ? "" : "s")} in Apple Music.");
        if (albums.Count == 0) return;

        // For information only: a pull no longer decides anything from this, since which albums are
        // new is a fact about this device's working list and not about the shortlist.
        var working = PlaylistStore.Open(2).Entries;
        int arriving = albums.Count(a => !working.Any(e => PlaylistStore.SameAlbum(e, a)));
        Console.WriteLine($"   {arriving} of them new to this Mac's working list.");

        var repo = CandidateRepository.Create();
        Console.WriteLine($"▶ Shortlist source: {(repo.SyncEnabled ? "Google Sheets" : $"local only ({repo.Status})")}");

        var shortlist = repo.SyncEnabled ? await repo.PullAsync() : null;
        if (shortlist is not { Count: > 0 }) shortlist = ReplacementCandidates.Load();

        // Broken out by status, because the shortlist screens count only the pending ones: a total
        // that drops as a screen syncs is usually decided rows being filtered out, not rows lost.
        int pending = shortlist.Count(a => a.Status == CandidateStatus.Pending);
        int kept = shortlist.Count(a => a.Status == CandidateStatus.Added);
        int dropped = shortlist.Count(a => a.Status == CandidateStatus.Declined);
        Console.WriteLine($"   {shortlist.Count} rows — {pending} pending, {kept} kept, {dropped} dropped.");

        // The whole playlist, exactly as a pull would ask it. Merged into the list we just read and
        // hold in memory — saved nowhere.
        var result = ShortlistIntake.Merge(shortlist, albums);

        Console.WriteLine($"▶ A pull would add {result.Added.Count} to the shortlist:");
        foreach (var album in result.Added) Console.WriteLine($"   + {album.Display}");
        foreach (var album in result.PreviouslyDropped) Console.WriteLine($"   · {album.Display} — dropped before, left alone");

        // Reading Discogs is as read-only as reading the sheet, and the years are half the point of
        // the exercise: it's what the shortlist sorts by and what keeping an album demands.
        int found = await ShortlistIntake.FillYearsAsync(result.Waiting!, line => Console.WriteLine($"   {line}"));
        Console.WriteLine($"▶ Years: {found} from Discogs, {result.StillWithoutAYear.Count} still blank.");
        foreach (var album in result.Waiting!)
            Console.WriteLine($"   {(album.Year.Length > 0 ? album.Year : "????")}  {album.Title} — {album.Artist}");

        Console.WriteLine("✓ Dry run only — nothing was written.");
    }
}
