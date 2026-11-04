using Avalonia;

namespace _1001AlbumHelper;

internal static class Program
{
    // Default: launch the desktop app. Use `dotnet run -- console` for the classic text menu.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("console") || args.Contains("--console"))
        {
            ConsoleMenu.RunAsync().GetAwaiter().GetResult();
            return;
        }

        // Headless check of the Google Sheets potentials sync (verifies access + seeds the sheet).
        if (args.Contains("synctest"))
        {
            SyncDiagnostic.RunAsync().GetAwaiter().GetResult();
            return;
        }

        // Headless, self-cleaning check of the mobile-safe REST Sheets client.
        if (args.Contains("resttest"))
        {
            RestSheetsClientDiagnostic.RunAsync().GetAwaiter().GetResult();
            return;
        }

        // Headless, self-cleaning check of the Mac's Apple Music path (scratch playlist, never
        // touches PLAYLIST1/PLAYLIST2).
        if (args.Contains("musictest"))
        {
            MusicAppDiagnostic.RunAsync().GetAwaiter().GetResult();
            return;
        }

        // Read-only: which Apple Music release a push would add for each "Artist|Title" given —
        // e.g. `lookup "Method Man|Tical" "Portishead|Dummy"`. Writes nothing anywhere.
        int lookupAt = Array.IndexOf(args, "lookup");
        if (lookupAt >= 0)
        {
            foreach (var pair in args.Skip(lookupAt + 1))
            {
                var parts = pair.Split('|', 2);
                if (parts.Length < 2) { Console.WriteLine($"✗ {pair}: expected \"Artist|Title\""); continue; }
                var found = AppleMusicCatalog.FindAlbumAsync(parts[0], parts[1]).GetAwaiter().GetResult();
                Console.WriteLine(found is null
                    ? $"✗ {parts[1]} — {parts[0]}: not in Apple Music"
                    : $"✓ {parts[1]} — {parts[0]} → {found.CollectionName} ({found.ArtistName}, "
                      + $"{found.TrackCount} tracks, {AppleMusicCatalog.EditionOf(found.CollectionName)})");
            }
            return;
        }

        // Read-only rehearsal of a PLAYLIST2 pull: says which albums would join the shortlist,
        // and writes nothing anywhere.
        if (args.Contains("intaketest"))
        {
            ShortlistIntakeDiagnostic.RunAsync().GetAwaiter().GetResult();
            return;
        }

        // Read-only rehearsal of the Apple Music favorites backfill: says how many albums off the
        // lists Apple Music is missing a heart on (and which), and writes nothing anywhere.
        if (args.Contains("favtest"))
        {
            FavoritesBackfillDiagnostic.RunAsync().GetAwaiter().GetResult();
            return;
        }

        // Headless favorites backfill: hearts every album on the finished list that Apple Music
        // hasn't already got one on, and mirrors them into the favorites playlist. Mac only.
        if (args.Contains("backfill-favorites"))
        {
            if (OperatingSystem.IsMacOS()) AppleMusic.Writer ??= new MusicAppPlaylistWriter();
            Operations.BackfillAppleMusicFavoritesAsync().GetAwaiter().GetResult();
            return;
        }

        // Headless genre backfill: fills column F on the master list for every rated album
        // missing one, via Discogs lookups. Resumable — safe to re-run.
        if (args.Contains("backfill-genres"))
        {
            Operations.BackfillGenresAsync(force: args.Contains("--force")).GetAwaiter().GetResult();
            return;
        }

        // Dev-only: show the mobile MainView in a phone-sized window instead of the desktop MainWindow.
        if (args.Contains("mobilepreview"))
        {
            App.PreviewMobile = true;
        }

        // The Insights page needs LiveCharts, which only this head references (see
        // InsightsPageHost) — wire the factory so MainWindow can show it.
        InsightsPageHost.Factory = () => new InsightsPage();

        // Apple Music, the Mac way: the iPhone's MediaPlayer framework doesn't exist here, so the
        // Mac drives the Music app over AppleScript instead. Windows/Linux get no writer, and the
        // playlist UI shows its Apple Music buttons as unavailable there, exactly as before.
        if (OperatingSystem.IsMacOS()) AppleMusic.Writer = new MusicAppPlaylistWriter();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Used by the Avalonia designer and by Main.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
