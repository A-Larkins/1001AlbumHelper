using Microsoft.Extensions.Configuration;

namespace _1001AlbumHelper;

/// <summary>
/// The core helper operations, shared by both the console menu and the desktop UI.
/// Every operation writes its progress to <see cref="Console"/> so callers can capture
/// it however they like (the console prints it directly; the desktop UI streams it to a log).
/// </summary>
public static class Operations
{
    // The folder that holds input/ and output/. Resolved so it works both when run via
    // `dotnet run` (bin/<config>/<tfm>/ -> project dir) and from a packaged .app bundle.
    public static string ProjectDir { get; } = ResolveDataDir();

    public static string InputDir => Path.Combine(ProjectDir, "input");
    public static string OutputDir => Path.Combine(ProjectDir, "output");

    private static string ResolveDataDir()
    {
        // Explicit override wins (the .app launcher sets this).
        var env = Environment.GetEnvironmentVariable("ALBUMHELPER_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return Path.GetFullPath(env);

        // Otherwise walk up from the executable looking for the project/data folder.
        // Markers: the .csproj (dev runs) or an existing input/ folder (a packaged app with
        // data placed beside it). We deliberately do NOT key on output/, since the app
        // creates that itself and it would make resolution circular.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "input"))
                || Directory.EnumerateFiles(dir, "*.csproj").Any())
                return dir;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return AppContext.BaseDirectory;
    }

    public const string RatedListFile = "1001 Albums You Must Hear Before You Die - 1001 albums.csv";
    public const string ReplacementListFile = "1001 Albums You Must Hear Before You Die - *my replacement albums.csv";
    public const string StarredOutputFile = "1001 Albums Larkins Thinks You Must Hear.csv";
    public const string ReplacementOutputFile = "my replacement albums.csv";
    public const string DiscogsOutputFile = "1001Albums.csv";

    /// <summary>Download the newest versions of every album list from Google Sheets into input/.</summary>
    public static async Task DownloadGoogleSheetsAsync()
    {
        Console.WriteLine("\n=== Downloading from Google Sheets ===\n");

        string spreadsheetId = "1UKN0bBNM3Hr5QaggiPcYUSRSt84o3dI0_06k_kIkY7k";

        // Sheet GIDs from the Google Sheets tabs
        var sheets = new[]
        {
            (gid: "0", name: RatedListFile),
            (gid: "729317458", name: "1001 Albums You Must Hear Before You Die - 1001 Albums Larkins Thinks You Must Hear.csv"),
            (gid: "1918952882", name: ReplacementListFile)
        };

        using var client = new HttpClient();
        Directory.CreateDirectory(InputDir);

        foreach (var sheet in sheets)
        {
            try
            {
                string url = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=csv&gid={sheet.gid}";
                Console.WriteLine($"Downloading {sheet.name}...");

                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string content = await response.Content.ReadAsStringAsync();
                string outputPath = Path.Combine(InputDir, sheet.name);

                File.WriteAllText(outputPath, content);
                Console.WriteLine($"✓ Saved to: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error downloading {sheet.name}: {ex.Message}");
            }
        }

        Console.WriteLine("\nDone! All sheets downloaded to input folder.");
    }

    /// <summary>Generate the "Albums Larkins Thinks You Must Hear" list from starred (⭐) albums.</summary>
    public static void CreateStarredAlbumsList()
    {
        Console.WriteLine("\n=== Creating Starred Albums List ===\n");
        var processor = new AlbumProcessor();
        string inputPath = Path.Combine(InputDir, RatedListFile);
        processor.CreateStarredAlbumsList(inputPath, StarredOutputFile);
    }

    /// <summary>Sort and renumber the replacement-albums list, writing a clean copy to output/.</summary>
    public static void RenumberReplacementAlbums()
    {
        Console.WriteLine("\n=== Renumbering Replacement Albums ===\n");
        var processor = new AlbumProcessor();
        string inputPath = Path.Combine(InputDir, ReplacementListFile);
        processor.RenumberReplacementAlbums(inputPath, ReplacementOutputFile);
    }

    /// <summary>Download the official 1001-albums list from Discogs and save it as a new local CSV.</summary>
    public static async Task FetchFreshListAsync()
    {
        Console.WriteLine("\n=== Fetching 1001 Albums from Discogs ===\n");
        Console.WriteLine("Fetching albums from Discogs API...");

        var apiClient = new DiscogsApiClient();
        var albums = await apiClient.FetchAlbumsFromListAsync("991847");

        if (albums.Count > 0)
        {
            // A fresh, uniquely-named file each time — never overwrites anything.
            string fileName = UniqueOutputName("1001Albums-discogs");
            Console.WriteLine($"\nFound {albums.Count} albums. Creating {fileName}…");
            var csvGenerator = new CsvGenerator();
            csvGenerator.CreateAlbumSpreadsheet(albums, fileName);
        }
        else
        {
            Console.WriteLine("No albums found.");
        }
    }

    /// <summary>Combine the newest Discogs list with your ratings into a new local merged CSV.</summary>
    public static void MergeRatingsWithDiscogsList()
    {
        Console.WriteLine("\n=== Merging Ratings with Discogs List ===\n");

        // Use the most recent Discogs list already in the output folder.
        string? discogsPath = LatestDiscogsList();
        if (discogsPath is null)
        {
            Console.WriteLine("No Discogs list found in the output folder. Run 'Fetch fresh list from Discogs' first.");
            return;
        }
        Console.WriteLine($"Using Discogs list: {Path.GetFileName(discogsPath)}");

        // Your existing rated list downloaded from Google Sheets
        string ratedPath = Path.Combine(InputDir, RatedListFile);

        // Write to a fresh, uniquely-named file — never overwrites anything.
        string outName = UniqueOutputName("1001Albums-merged");
        var processor = new AlbumProcessor();
        processor.MergeRatingsWithDiscogsList(discogsPath, ratedPath, outName);
    }

    // A unique, timestamped output file name so Discogs runs never overwrite existing files.
    private static string UniqueOutputName(string baseName)
    {
        Directory.CreateDirectory(OutputDir);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string name = $"{baseName}-{stamp}.csv";
        int i = 2;
        while (File.Exists(Path.Combine(OutputDir, name)))
            name = $"{baseName}-{stamp}-{i++}.csv";
        return name;
    }

    private static string? LatestDiscogsList()
    {
        if (!Directory.Exists(OutputDir)) return null;
        return Directory.EnumerateFiles(OutputDir, "1001Albums-discogs-*.csv")
            .Concat(Directory.EnumerateFiles(OutputDir, DiscogsOutputFile)) // legacy name, if present
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    // ----- Google Sheets sync -------------------------------------------------

    /// <summary>Build the starred list and the renumbered replacements, then push both to Google Sheets.</summary>
    public static async Task SyncBothToSheetsAsync()
    {
        Console.WriteLine("\n=== Build & sync BOTH lists to Google Sheets ===\n");

        var cfg = LoadSheetsConfig();
        var writer = await CreateWriterAsync(cfg);
        if (writer is null) return;

        // Pull the latest from the sheet first so we never write back stale data.
        Console.WriteLine("\n— Refreshing input from Google Sheets —");
        await DownloadGoogleSheetsAsync();

        Console.WriteLine("\n— Building 'Must Hear' list —");
        CreateStarredAlbumsList();
        await PushToSheetAsync(writer, StarredOutputFile, cfg.StarredTab);

        Console.WriteLine("\n— Renumbering replacement albums —");
        RenumberReplacementAlbums();
        await PushToSheetAsync(writer, ReplacementOutputFile, cfg.ReplacementsTab);

        Console.WriteLine("\n✓ Both lists synced to Google Sheets.");
    }

    /// <summary>Build the starred list and push it to Google Sheets.</summary>
    public static async Task SyncStarredToSheetAsync()
    {
        Console.WriteLine("\n=== Build & sync 'Must Hear' list to Google Sheets ===\n");

        var cfg = LoadSheetsConfig();
        var writer = await CreateWriterAsync(cfg);
        if (writer is null) return;

        Console.WriteLine("\n— Refreshing input from Google Sheets —");
        await DownloadGoogleSheetsAsync();

        CreateStarredAlbumsList();
        await PushToSheetAsync(writer, StarredOutputFile, cfg.StarredTab);
        Console.WriteLine("\n✓ Synced to Google Sheets.");
    }

    /// <summary>Renumber the replacement albums and push them to Google Sheets.</summary>
    public static async Task SyncReplacementsToSheetAsync()
    {
        Console.WriteLine("\n=== Renumber & sync replacement albums to Google Sheets ===\n");

        var cfg = LoadSheetsConfig();
        var writer = await CreateWriterAsync(cfg);
        if (writer is null) return;

        Console.WriteLine("\n— Refreshing input from Google Sheets —");
        await DownloadGoogleSheetsAsync();

        RenumberReplacementAlbums();
        await PushToSheetAsync(writer, ReplacementOutputFile, cfg.ReplacementsTab);
        Console.WriteLine("\n✓ Synced to Google Sheets.");
    }

    /// <summary>Read-only check: authenticate and list the configured spreadsheet's tabs.</summary>
    public static async Task ListSheetTabsAsync()
    {
        Console.WriteLine("\n=== Google Sheets: connection check ===\n");

        var cfg = LoadSheetsConfig();
        var writer = await CreateWriterAsync(cfg);
        if (writer is null) return;

        try
        {
            var tabs = await writer.GetTabTitlesAsync();
            Console.WriteLine($"Spreadsheet {cfg.SpreadsheetId} has {tabs.Count} tab(s):");
            foreach (var t in tabs)
                Console.WriteLine($"  • {t}");
            Console.WriteLine();
            Console.WriteLine($"Configured targets → StarredTab: \"{cfg.StarredTab}\"   ReplacementsTab: \"{cfg.ReplacementsTab}\"");
            Console.WriteLine(tabs.Contains(cfg.StarredTab)
                ? $"✓ \"{cfg.StarredTab}\" exists — the starred list will overwrite it."
                : $"⚠️  \"{cfg.StarredTab}\" not found — it will be created as a new tab.");
            Console.WriteLine(tabs.Contains(cfg.ReplacementsTab)
                ? $"✓ \"{cfg.ReplacementsTab}\" exists — replacements will overwrite it."
                : $"⚠️  \"{cfg.ReplacementsTab}\" not found — it will be created as a new tab.");
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            Console.WriteLine("✗ The signed-in Google account can't read this sheet. Check that it owns or is shared on it.");
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.WriteLine("✗ Spreadsheet not found. Double-check GoogleSheets:SpreadsheetId in appsettings.json.");
        }
    }

    private static async Task<GoogleSheetsWriter?> CreateWriterAsync(GoogleSheetsConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.SpreadsheetId) || cfg.SpreadsheetId.StartsWith("PUT_"))
        {
            Console.WriteLine("⚠️  Google Sheets isn't configured yet.");
            Console.WriteLine("   Set \"GoogleSheets:SpreadsheetId\" in appsettings.json to your test sheet's ID.");
            return null;
        }

        string credPath = Path.Combine(ProjectDir, cfg.CredentialsFile);
        if (!File.Exists(credPath))
        {
            Console.WriteLine("⚠️  Google OAuth credentials were not found at:");
            Console.WriteLine($"     {credPath}");
            Console.WriteLine("   Download an OAuth 'Desktop app' credentials.json from Google Cloud and place it there.");
            return null;
        }

        Console.WriteLine("Authenticating with Google… (a browser window opens the first time — sign in and approve).");
        try
        {
            var writer = await GoogleSheetsWriter.CreateAsync(
                cfg.SpreadsheetId, credPath, Path.Combine(ProjectDir, ".google-sheets-token"));
            Console.WriteLine("✓ Authenticated with Google.");
            return writer;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Google authentication failed: {ex.Message}");
            return null;
        }
    }

    private static async Task PushToSheetAsync(GoogleSheetsWriter writer, string outputFileName, string tabName)
    {
        string path = Path.Combine(OutputDir, outputFileName);
        if (!File.Exists(path))
        {
            Console.WriteLine($"✗ Generated file not found: {path}");
            return;
        }

        var rows = ReadCsvRows(path);
        Console.WriteLine($"Uploading {rows.Count} rows to tab \"{tabName}\"…");
        try
        {
            await writer.WriteTabAsync(tabName, rows);
            Console.WriteLine($"✓ Wrote {rows.Count} rows to \"{tabName}\".");
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            Console.WriteLine("✗ The signed-in Google account doesn't have permission to edit this sheet.");
            Console.WriteLine("   Share the sheet as Editor with the account you approved, or use a sheet that account owns.");
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.WriteLine("✗ Spreadsheet not found. Double-check GoogleSheets:SpreadsheetId in appsettings.json.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to write \"{tabName}\": {ex.Message}");
        }
    }

    private static GoogleSheetsConfig LoadSheetsConfig()
    {
        // Read the copy next to the executable first, then let the live copy in the data
        // folder override it — so editing appsettings.json takes effect without a rebuild
        // (matters for the packaged .app, whose baked-in copy would otherwise be stale).
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
            .AddJsonFile(Path.Combine(ProjectDir, "appsettings.json"), optional: true)
            .Build();

        return new GoogleSheetsConfig(
            SpreadsheetId: config["GoogleSheets:SpreadsheetId"] ?? "",
            StarredTab: config["GoogleSheets:StarredTab"] ?? "Must Hear",
            ReplacementsTab: config["GoogleSheets:ReplacementsTab"] ?? "Replacements",
            AlbumsTab: config["GoogleSheets:AlbumsTab"] ?? "1001 albums",
            CredentialsFile: config["GoogleSheets:CredentialsFile"] ?? "credentials.json");
    }

    /// <summary>
    /// Opens a rating session over the master album list. Returns null (having explained why to
    /// the log) when Google Sheets isn't configured or authentication fails.
    /// </summary>
    public static async Task<RatingSession?> OpenRatingSessionAsync()
    {
        var cfg = LoadSheetsConfig();
        var writer = await CreateWriterAsync(cfg);
        if (writer is null) return null;

        Console.WriteLine($"Reading \"{cfg.AlbumsTab}\"…");
        try
        {
            var session = await RatingSession.LoadAsync(writer, cfg.AlbumsTab, cfg.StarredTab);
            Console.WriteLine($"✓ Loaded {session.TotalAlbums} albums.");
            return session;
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.WriteLine($"✗ Tab \"{cfg.AlbumsTab}\" not found. Set GoogleSheets:AlbumsTab in appsettings.json.");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Couldn't load the album list: {ex.Message}");
            return null;
        }
    }

    /// <summary>What a sync would have to fix. All counts come from a read-only pass.</summary>
    public sealed record SyncStatus(
        IReadOnlyList<AlbumEntry> MissingStars,
        int LooseReplacements,
        bool ReplacementsMisnumbered,
        bool MustHearMisnumbered,
        IReadOnlyList<NumberedList.Row> NoLongerStarred,
        IReadOnlyList<NumberedList.Row> ReplacementsAlreadyIn1001,
        string? Error = null)
    {
        public bool NeedsSync => MissingStars.Count > 0 || LooseReplacements > 0
                              || ReplacementsMisnumbered || MustHearMisnumbered
                              || NoLongerStarred.Count > 0 || ReplacementsAlreadyIn1001.Count > 0;

        public string Summary()
        {
            if (Error is not null) return Error;
            if (!NeedsSync) return "Everything's in sync.";

            var bits = new List<string>();
            if (MissingStars.Count > 0) bits.Add($"{MissingStars.Count} ⭐ to add");
            if (NoLongerStarred.Count > 0) bits.Add($"{NoLongerStarred.Count} no longer ⭐ to drop");
            if (ReplacementsAlreadyIn1001.Count > 0)
                bits.Add($"{ReplacementsAlreadyIn1001.Count} replacement(s) already in the 1001");
            if (LooseReplacements > 0) bits.Add($"{LooseReplacements} unnumbered");
            if (ReplacementsMisnumbered || MustHearMisnumbered) bits.Add("renumbering needed");
            return string.Join(" · ", bits);
        }
    }

    /// <summary>Read-only: works out whether a sync is needed. Never modifies the spreadsheet.</summary>
    public static async Task<SyncStatus> CheckSyncStatusAsync(bool quiet = false)
    {
        var empty = Array.Empty<AlbumEntry>();
        var noRows = Array.Empty<NumberedList.Row>();

        var cfg = LoadSheetsConfig();
        GoogleSheetsWriter? writer;
        try
        {
            writer = quiet ? await CreateWriterQuietlyAsync(cfg) : await CreateWriterAsync(cfg);
        }
        catch (Exception ex)
        {
            return new SyncStatus(empty, 0, false, false, noRows, noRows, $"Couldn't reach Google Sheets: {ex.Message}");
        }
        if (writer is null)
            return new SyncStatus(empty, 0, false, false, noRows, noRows, "Google Sheets isn't configured.");

        try
        {
            var master = await RatingSession.LoadAsync(writer, cfg.AlbumsTab, cfg.StarredTab);
            var mustHear = await NumberedList.ReadAsync(writer, cfg.StarredTab);
            var replacements = await NumberedList.ReadAsync(writer, cfg.ReplacementsTab);

            var starred = master.AllAlbums.Where(a => a.Rating == RatingSession.Starred).ToList();
            var missing = starred
                .Where(a => NumberedList.Find(mustHear, a.Title, a.Artist) is null)
                .ToList();

            // The reverse direction: on Must Hear but no longer starred upstream.
            var stale = mustHear.Rows
                .Where(r => !starred.Any(a =>
                    NumberedList.Matches(a.Title, r.Title) && NumberedList.Matches(a.Artist, r.Artist)))
                .ToList();

            // Replacements exist to cover gaps in the 1001 list, so an entry on both is redundant.
            var redundant = replacements.Rows
                .Where(r => master.AllAlbums.Any(a =>
                    NumberedList.Matches(a.Title, r.Title) && NumberedList.Matches(a.Artist, r.Artist)))
                .ToList();

            return new SyncStatus(
                missing,
                replacements.Unnumbered.Count,
                NumberedList.NumberingIsBroken(replacements) || replacements.Unnumbered.Count > 0,
                NumberedList.NumberingIsBroken(mustHear) || mustHear.Unnumbered.Count > 0,
                stale,
                redundant);
        }
        catch (Exception ex)
        {
            return new SyncStatus(empty, 0, false, false, noRows, noRows, $"Sync check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reconciles both derived lists with the master list: adds ⭐ albums missing from Must Hear,
    /// places any unnumbered replacement rows by year, and renumbers both lists.
    /// <para>
    /// Deliberately additive — an album on Must Hear whose ⭐ was removed upstream is reported but
    /// left in place, since deleting someone's row on a guess is not a repair.
    /// </para>
    /// </summary>
    public static async Task SyncAllAsync()
    {
        Console.WriteLine("\n=== Sync all ===\n");

        var cfg = LoadSheetsConfig();
        var writer = await CreateWriterAsync(cfg);
        if (writer is null) return;

        var master = await RatingSession.LoadAsync(writer, cfg.AlbumsTab, cfg.StarredTab);
        var starred = master.AllAlbums.Where(a => a.Rating == RatingSession.Starred).ToList();
        Console.WriteLine($"Master list: {master.TotalAlbums} albums, {starred.Count} starred.");

        // 1. Stars missing from Must Hear.
        var mustHear = await NumberedList.ReadAsync(writer, cfg.StarredTab);
        var missing = starred.Where(a => NumberedList.Find(mustHear, a.Title, a.Artist) is null).ToList();

        if (missing.Count == 0)
        {
            Console.WriteLine($"✓ \"{cfg.StarredTab}\" already has every ⭐.");
        }
        else
        {
            Console.WriteLine($"Adding {missing.Count} ⭐ to \"{cfg.StarredTab}\"…");
            foreach (var album in missing)
            {
                if (!int.TryParse(album.Year, out int year))
                {
                    Console.WriteLine($"   ⚠️ skipped “{album.Title}” — unreadable year “{album.Year}”.");
                    continue;
                }
                int pos = await NumberedList.InsertByYearAsync(
                    writer, cfg.StarredTab, album.Title, album.Artist, year);
                Console.WriteLine($"   + #{pos} {album.Title} — {album.Artist} ({year})");
            }
        }

        // 2. Must Hear: drop anything no longer starred upstream, place loose rows, renumber.
        var mhPlan = await NumberedList.ApplyAsync(writer, cfg.StarredTab,
            row => !starred.Any(a => NumberedList.Matches(a.Title, row.Title)
                                  && NumberedList.Matches(a.Artist, row.Artist)));
        ReportPlan(cfg.StarredTab, mhPlan, "no longer ⭐ on the 1001 list");

        // 3. Replacements: drop anything the 1001 list already covers, place loose rows, renumber.
        var onMaster = master.AllAlbums;
        var replPlan = await NumberedList.ApplyAsync(writer, cfg.ReplacementsTab,
            row => onMaster.Any(a => NumberedList.Matches(a.Title, row.Title)
                                  && NumberedList.Matches(a.Artist, row.Artist)));
        ReportPlan(cfg.ReplacementsTab, replPlan, "already on the 1001 list");

        Console.WriteLine("\n✓ Sync complete.");
    }

    private static void ReportPlan(string tab, NumberedList.Plan plan, string removalReason)
    {
        if (!plan.Changed)
        {
            Console.WriteLine($"✓ \"{tab}\": already correct.");
            return;
        }

        foreach (var r in plan.Removed)
            Console.WriteLine($"   − removed #{r.Number} {r.Title} — {r.Artist} ({removalReason})");
        foreach (var r in plan.Placed)
            Console.WriteLine($"   ↳ placed “{r.Title}” ({r.Year}) into its year block");

        Console.WriteLine($"✓ \"{tab}\": {plan.Keep.Count} rows, renumbered from 1"
                        + (plan.Removed.Count > 0 ? $", {plan.Removed.Count} removed" : "") + ".");
    }

    /// <summary>Authenticates without the chatty console output, for the startup check.</summary>
    private static async Task<GoogleSheetsWriter?> CreateWriterQuietlyAsync(GoogleSheetsConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.SpreadsheetId) || cfg.SpreadsheetId.StartsWith("PUT_"))
            return null;
        string credPath = Path.Combine(ProjectDir, cfg.CredentialsFile);
        if (!File.Exists(credPath)) return null;

        return await GoogleSheetsWriter.CreateAsync(
            cfg.SpreadsheetId, credPath, Path.Combine(ProjectDir, ".google-sheets-token"));
    }

    /// <summary>How an attempt to add a replacement album turned out.</summary>
    public enum AddOutcome
    {
        Added,
        AlreadyInReplacements,
        AlreadyIn1001,
        NotConfigured,
        Failed,
    }

    /// <summary><paramref name="Detail"/> explains a rejection; <paramref name="Warning"/> is advisory only.</summary>
    public sealed record AddResult(AddOutcome Outcome, int? Position, string? Detail, string? Warning = null);

    /// <summary>
    /// Adds an album to the replacements tab, slotted in at the end of its year block, and
    /// renumbers that list.
    /// <para>
    /// Refuses if the album is already on the replacements list, or if it's already on the master
    /// 1001 list — the replacements tab exists for albums the 1001 doesn't cover, so an entry that
    /// appears on both is a mistake. A same-title-different-artist hit is reported as a warning
    /// rather than a refusal, since those are often genuinely different albums.
    /// </para>
    /// </summary>
    public static async Task<AddResult> AddReplacementAlbumAsync(string title, string artist, int year)
    {
        var cfg = LoadSheetsConfig();
        var writer = await CreateWriterAsync(cfg);
        if (writer is null)
            return new AddResult(AddOutcome.NotConfigured, null,
                "Google Sheets isn't configured or authentication failed.");

        try
        {
            // 1. Already a replacement?
            var replacements = await NumberedList.ReadAsync(writer, cfg.ReplacementsTab);
            if (NumberedList.Find(replacements, title, artist) is { } dupe)
            {
                string detail = $"Already on your replacements list at #{dupe.Number} " +
                                $"— “{dupe.Title}” by {dupe.Artist} ({dupe.Year}).";
                Console.WriteLine($"⚠️  {detail}");
                return new AddResult(AddOutcome.AlreadyInReplacements, null, detail);
            }

            // 2. Already on the canonical 1001 list?
            var master = await RatingSession.LoadAsync(writer, cfg.AlbumsTab, cfg.StarredTab);
            var onMaster = master.AllAlbums.FirstOrDefault(a =>
                NumberedList.Matches(a.Title, title) && NumberedList.Matches(a.Artist, artist));
            if (onMaster is not null)
            {
                string rating = string.IsNullOrWhiteSpace(onMaster.Rating)
                    ? "not yet rated"
                    : $"rated {onMaster.Rating}";
                string detail = $"Already on the 1001 list at #{onMaster.Number} ({rating}) " +
                                $"— “{onMaster.Title}” by {onMaster.Artist} ({onMaster.Year}). " +
                                "Replacements are for albums the 1001 list doesn't have.";
                Console.WriteLine($"⚠️  {detail}");
                return new AddResult(AddOutcome.AlreadyIn1001, null, detail);
            }

            // 3. Same title, different artist — worth flagging, not worth blocking.
            var nearReplacements = NumberedList.FindByTitleOnly(replacements, title, artist);
            var nearMaster = master.AllAlbums
                .Where(a => NumberedList.Matches(a.Title, title) && !NumberedList.Matches(a.Artist, artist))
                .Select(a => $"#{a.Number} by {a.Artist} on the 1001 list");
            var near = nearReplacements
                .Select(r => $"#{r.Number} by {r.Artist} on your replacements")
                .Concat(nearMaster)
                .ToList();

            string? warning = near.Count > 0
                ? $"Note: “{title}” also appears as {string.Join("; ", near)}."
                : null;
            if (warning is not null) Console.WriteLine($"ℹ️  {warning}");

            int position = await NumberedList.InsertByYearAsync(
                writer, cfg.ReplacementsTab, title, artist, year);
            Console.WriteLine($"✓ Added “{title}” ({year}) to \"{cfg.ReplacementsTab}\" at #{position}.");
            return new AddResult(AddOutcome.Added, position, null, warning);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Couldn't add “{title}”: {ex.Message}");
            return new AddResult(AddOutcome.Failed, null, ex.Message);
        }
    }

    private static IList<IList<object>> ReadCsvRows(string path)
    {
        var rows = new List<IList<object>>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            rows.Add(SplitCsvLine(line).Cast<object>().ToList());
        }
        return rows;
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var field = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(field.ToString());
                field.Clear();
            }
            else field.Append(c);
        }
        result.Add(field.ToString());
        return result.ToArray();
    }

    private sealed record GoogleSheetsConfig(
        string SpreadsheetId, string StarredTab, string ReplacementsTab, string AlbumsTab, string CredentialsFile);
}
