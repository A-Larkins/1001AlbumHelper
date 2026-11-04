using System;
using System.Linq;
using System.Threading.Tasks;

namespace _1001AlbumHelper;

/// <summary>
/// A dry run of the Apple Music favorites backfill. Like <see cref="ShortlistIntakeDiagnostic"/>
/// it writes <em>nothing at all</em> — no favorite is set, no playlist is touched — so it can be
/// run first to see exactly what the real backfill would do, and to how many albums.
///
/// <para>
/// Worth running before the real thing for a second reason: the backfill's cost is one Music-app
/// library search per album it has to touch, and this says how many that is before you commit to
/// sitting through them.
/// </para>
///
/// <para>Run it with <c>dotnet run --project 1001AlbumHelper.Desktop -- favtest</c>.</para>
/// </summary>
public static class FavoritesBackfillDiagnostic
{
    /// <summary>How many albums to name individually before falling back to the count alone.</summary>
    private const int Listed = 40;

    public static async Task RunAsync()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Console.WriteLine("✗ Apple Music favorites are macOS-only — see PROJECT.md §5.");
            return;
        }
        AppleMusic.Writer ??= new MusicAppPlaylistWriter();

        Console.WriteLine("▶ Reading the lists from Google Sheets and the favorites from the Music app…");
        var plan = await Operations.PlanFavoritesBackfillAsync();
        if (plan.Error is not null)
        {
            Console.WriteLine($"✗ {plan.Error}");
            await ReportMusicOnlyAsync();
            return;
        }

        Console.WriteLine($"   Apple Music: {plan.FavoritedInLibrary} favorited albums, "
                          + $"{plan.InPlaylist} in “{AppleMusic.FavoritesPlaylistName}”.");
        Console.WriteLine($"   {plan.Summary}");

        Report("would be favorited", plan.Items.Where(i => i.NeedsFavorite));
        Report($"would be added to “{AppleMusic.FavoritesPlaylistName}”", plan.Items.Where(i => i.NeedsPlaylist));

        Console.WriteLine($"▶ {plan.Work.Count} album{(plan.Work.Count == 1 ? "" : "s")} to touch — "
                          + "each is one Music app library search, so budget a second or two apiece.");
        Console.WriteLine("✓ Dry run only — nothing was written.");
    }

    /// <summary>
    /// The Apple Music half on its own, for when the Google Sheets half is what failed — most often
    /// an expired OAuth token. Half a diagnostic still tells you which half is broken, and this is
    /// the half the backfill can't be checked any other way.
    /// </summary>
    private static async Task ReportMusicOnlyAsync()
    {
        Console.WriteLine("▶ Checking the Apple Music side on its own…");
        try
        {
            var favorited = await AppleMusic.Writer!.ReadFavoritedAlbumsAsync();
            var inPlaylist = await AppleMusic.Writer!.ReadAlbumsAsync(AppleMusic.FavoritesPlaylistName);
            Console.WriteLine($"   ✓ {favorited.Count} favorited albums in the library.");
            Console.WriteLine($"   {(inPlaylist.Count > 0 ? "✓" : "⚠")} {inPlaylist.Count} albums in "
                              + $"“{AppleMusic.FavoritesPlaylistName}”"
                              + (inPlaylist.Count == 0 ? " — check the playlist exists under that name." : "."));
        }
        catch (Exception ex) { Console.WriteLine($"   ✗ {ex.Message}"); }
    }

    private static void Report(string what, System.Collections.Generic.IEnumerable<Operations.FavoritesBackfillItem> items)
    {
        var albums = items.ToList();
        Console.WriteLine($"▶ {albums.Count} {what}:");
        foreach (var item in albums.Take(Listed))
            Console.WriteLine($"   + {item.Album.Display}");
        if (albums.Count > Listed) Console.WriteLine($"   … and {albums.Count - Listed} more");
    }
}
