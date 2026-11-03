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
            try
            {
                if (Directory.Exists(Path.Combine(dir, "input"))
                    || Directory.EnumerateFiles(dir, "*.csproj").Any())
                    return dir;
            }
            // On a sandboxed platform (iOS) enumerating a parent directory is "Operation not
            // permitted" — stop walking rather than letting it kill this whole type initializer,
            // which any code that touches Operations would then trip over.
            catch (IOException) { break; }
            catch (UnauthorizedAccessException) { break; }

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

    /// <summary>
    /// Fills in column F (Genre) on the master list for every rated album that doesn't have one
    /// yet, via Discogs lookups. Resumable — already-filled rows are skipped by default, so
    /// re-running only costs a request per still-blank row (e.g. newly rated albums since the
    /// last run). Pass <paramref name="force"/> to re-fetch every rated row regardless — e.g.
    /// after a change to what <see cref="DiscogsApiClient"/> extracts as "genre".
    /// </summary>
    public static async Task BackfillGenresAsync(bool force = false)
    {
        var cfg = LoadSheetsConfig();
        var writer = await CreateWriterAsync(cfg);
        if (writer is null) return;

        var discogs = DiscogsApiClient.TryCreate();
        if (discogs is null)
        {
            Console.WriteLine("⚠️  Discogs isn't configured (missing/placeholder token in appsettings.json).");
            return;
        }

        Console.WriteLine($"Reading \"{cfg.AlbumsTab}\"…");
        var session = await RatingSession.LoadAsync(writer, cfg.AlbumsTab, cfg.StarredTab);

        var targets = session.AllAlbums
            .Where(a => !string.IsNullOrWhiteSpace(a.Rating) && (force || a.Genre.Length == 0))
            .ToList();

        Console.WriteLine(force
            ? $"{targets.Count} rated album(s) — re-fetching all (--force).\n"
            : $"{targets.Count} rated album(s) still need a genre.\n");

        int found = 0, missed = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            var album = targets[i];
            Console.Write($"[{i + 1}/{targets.Count}] {album.Title} — {album.Artist}… ");

            AlbumSuggestion? suggestion = null;
            for (int attempt = 1; attempt <= 4; attempt++)
            {
                try
                {
                    suggestion = await discogs.FindAlbumAsync(album.Title, album.Artist);
                    break;
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("429") && attempt < 4)
                {
                    // Discogs' 60/min budget is shared across every request FindAlbumAsync's own
                    // rungs make, so a long run can trip it even at ~1/sec average. Back off hard
                    // and retry rather than silently leaving the album blank.
                    var wait = TimeSpan.FromSeconds(10 * attempt);
                    Console.WriteLine($"rate-limited, waiting {wait.TotalSeconds:0}s… ");
                    await Task.Delay(wait);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ lookup failed: {ex.Message}");
                    break;
                }
            }

            if (suggestion is not null && suggestion.Genre.Length > 0)
            {
                await writer.UpdateCellAsync(cfg.AlbumsTab, $"F{album.SheetRow}", suggestion.Genre);
                Console.WriteLine(suggestion.Genre);
                found++;
            }
            else
            {
                Console.WriteLine("not found — skipped");
                missed++;
            }

            // A bit under Discogs' 60/min budget — FindAlbumAsync's own rungs spend from the same
            // budget when an album isn't found on the first try, so this leaves headroom for that.
            if (i < targets.Count - 1) await Task.Delay(TimeSpan.FromMilliseconds(1500));
        }

        Console.WriteLine($"\n✓ Done. {found} filled in, {missed} not found on Discogs.");
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

    /// <summary>All three lists as read from the sheet, for read-only consumers.</summary>
    public sealed record AllLists(
        IReadOnlyList<AlbumEntry> Master,
        IReadOnlyList<NumberedList.Row> MustHear,
        IReadOnlyList<NumberedList.Row> Replacements,
        string? Error = null);

    /// <summary>Reads every list in one pass. Read-only — nothing is written.</summary>
    public static async Task<AllLists> LoadAllListsAsync()
    {
        var noAlbums = Array.Empty<AlbumEntry>();
        var noRows = Array.Empty<NumberedList.Row>();

        var cfg = LoadSheetsConfig();
        GoogleSheetsWriter? writer;
        try
        {
            writer = await CreateWriterQuietlyAsync(cfg);
        }
        catch (Exception ex)
        {
            return new AllLists(noAlbums, noRows, noRows, $"Couldn't reach Google Sheets: {ex.Message}");
        }
        if (writer is null)
            return new AllLists(noAlbums, noRows, noRows, "Google Sheets isn't configured.");

        try
        {
            var master = await RatingSession.LoadAsync(writer, cfg.AlbumsTab, cfg.StarredTab);
            var mustHear = await NumberedList.ReadAsync(writer, cfg.StarredTab);
            var replacements = await NumberedList.ReadAsync(writer, cfg.ReplacementsTab);
            return new AllLists(master.AllAlbums, mustHear.Rows, replacements.Rows);
        }
        catch (Exception ex)
        {
            return new AllLists(noAlbums, noRows, noRows, $"Couldn't read the lists: {ex.Message}");
        }
    }

    /// <summary>
    /// Exports the combined list — ⭐ picks kept from the 1001, then the replacements — as a
    /// numbered PDF. Returns the file path, or null if it couldn't be produced.
    /// </summary>
    public static async Task<string?> ExportPdfAsync(string? destination = null)
    {
        Console.WriteLine("\n=== Export to PDF ===\n");

        var lists = await LoadAllListsAsync();
        if (lists.Error is not null)
        {
            Console.WriteLine($"✗ {lists.Error}");
            return null;
        }

        var cfg = LoadSheetsConfig();
        string path = destination ?? Path.Combine(
            OutputDir, $"1001 Albums Larkins Thinks You Must Hear - {DateTime.Now:yyyy-MM-dd}.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        Console.WriteLine($"Must Hear: {lists.MustHear.Count} · replacements: {lists.Replacements.Count}");
        try
        {
            PdfExporter.Write(path, lists.MustHear, lists.Replacements, cfg.StarredTab);
            Console.WriteLine($"✓ Wrote {lists.MustHear.Count + lists.Replacements.Count} albums to:");
            Console.WriteLine($"   {path}");
            return path;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Couldn't write the PDF: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Renders the same breakdowns the Analytics window charts — ratings and shortlist alike —
    /// as a PDF snapshot. Returns the file path, or null if it couldn't be produced.
    /// </summary>
    public static async Task<string?> ExportAnalyticsPdfAsync(string? destination = null)
    {
        Console.WriteLine("\n=== Export Analytics to PDF ===\n");

        var session = await OpenRatingSessionAsync();
        if (session is null) return null;

        var rated = session.AllAlbums.Where(a => AlbumAnalytics.ScoreFor(a.Rating) is not null).ToList();
        var summary = AlbumAnalytics.Summarize(session.AllAlbums);

        var repo = CandidateRepository.Create();
        try
        {
            var remote = await repo.PullAsync();
            if (remote is { Count: > 0 }) ReplacementCandidates.Save(remote);
        }
        catch { /* local cache is still a fine fallback */ }
        var candidates = ReplacementCandidates.Load();
        int pending = candidates.Count(c => c.Status == CandidateStatus.Pending);

        string path = destination ?? Path.Combine(
            OutputDir, $"Taste Patterns Report - {DateTime.Now:yyyy-MM-dd}.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            AnalyticsPdfExporter.Write(
                path, summary,
                AlbumAnalytics.DecadeBreakdown(rated),
                AlbumAnalytics.GenreBreakdown(rated),
                AlbumAnalytics.RatingDistribution(rated),
                AlbumAnalytics.BestGenres(rated),
                pending,
                AlbumAnalytics.CandidateDecadeBreakdown(candidates),
                AlbumAnalytics.CandidateGenreBreakdown(candidates));
            Console.WriteLine($"✓ Wrote the taste patterns report to:\n   {path}");
            return path;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Couldn't write the PDF: {ex.Message}");
            return null;
        }
    }

    // ---------- Keeping an album straight off a playlist ----------

    /// <summary>What keeping an album straight off Playlist 2 did.</summary>
    /// <param name="Position">Where it landed on the replacements list, when it landed.</param>
    /// <param name="Message">One line for the caller's status area, already phrased for a person.</param>
    /// <param name="Shortlisted">
    /// Whether the shortlist now records the decision. False only when the replacements add was
    /// refused, or when writing the shortlist itself failed — the two lists are written in that
    /// order deliberately (see below).
    /// </param>
    public sealed record PlaylistKeepResult(
        AddOutcome Outcome, int? Position, string Message, bool Shortlisted);

    /// <summary>
    /// Puts an album from Playlist 2 on to <em>both</em> lists in one press: the replacements list
    /// proper, slotted into its year block and renumbered, and the potentials shortlist, which
    /// records that the decision was made. It's the shortlist's "Keep" button, reached from the
    /// playlist rather than from the shortlist tab — same two writes, same order, same records left
    /// behind, so an album kept this way is indistinguishable afterwards from one kept the long way.
    /// <para>
    /// The distinction against "＋ Shortlist" is a real one and both are worth having: the shortlist
    /// is for an album you've written down but not ruled on, this is for one you've already decided
    /// about while it was playing.
    /// </para>
    /// <para>
    /// The replacements list is written first and the shortlist second, because the replacements
    /// list is the one that can refuse — an album already on the 1001 doesn't belong on it, and the
    /// shortlist must not record a decision that didn't happen. The other order would leave a row
    /// marked "kept" pointing at a list it never reached.
    /// </para>
    /// <para>
    /// The title is trimmed of its edition furniture first, exactly as an intake does: Apple Music
    /// stocks "Cassini (5th Anniversary Remaster)", and nobody wants that on the finished list.
    /// </para>
    /// </summary>
    /// <param name="cacheLocally">
    /// True on the desktop, which keeps a local JSON copy of the shortlist; false on the phone,
    /// where the Potentials sheet is the only place a candidate can be saved.
    /// </param>
    public static async Task<PlaylistKeepResult> KeepFromPlaylistAsync(
        PlaylistEntry album,
        ISheetsClient client, string replacementsTab, string albumsTab, string starredTab,
        bool cacheLocally, Action<string>? progress = null)
    {
        // The album's own name, not the pressing's — see the note above.
        string title = DiscogsApiClient.BareTitle(album.Title);
        string artist = album.Artist;

        // A year is what the replacements list slots by, so it isn't optional here. Apple Music's
        // read-back carries none, so the common case is a lookup rather than a value we already hold.
        int year;
        if (!TryYear(album.Year, out year))
        {
            progress?.Invoke($"Looking up the year for “{title}” on Discogs…");
            string? found = null;
            try
            {
                var discogs = DiscogsApiClient.TryCreate();
                if (discogs is not null) found = (await discogs.FindAlbumAsync(title, artist))?.Year;
            }
            catch { /* treated the same as "no year found" — the message below says what to do */ }

            if (!TryYear(found, out year))
                return new PlaylistKeepResult(AddOutcome.Failed, null,
                    $"No year for “{title}” — put it on the shortlist and type one in there.", false);
        }

        progress?.Invoke($"Adding “{title}” ({year}) to the replacements list…");
        var add = await AddReplacementAlbumAsync(client, replacementsTab, albumsTab, starredTab,
                                                 title, artist, year);

        // A refusal must not be written down as a decision — see ReachedTheList.
        if (!ReachedTheList(add.Outcome))
        {
            string why = add.Detail ?? "Couldn't add it to the replacements list.";
            return new PlaylistKeepResult(add.Outcome, null, $"⚠ Not kept — {why}", false);
        }

        progress?.Invoke($"Recording the decision on the shortlist…");
        string? shortlistProblem = await RecordKeptOnShortlistAsync(title, artist, year, cacheLocally);

        string landed = add.Outcome == AddOutcome.Added
            ? $"✓ “{title}” ({year}) is on the replacements list at #{add.Position}."
            : $"“{title}” was already on the replacements list. {add.Detail}";

        string note = shortlistProblem is null
            ? $"{landed} Marked kept on the shortlist."
            : $"{landed} ⚠ The shortlist wasn't updated — {shortlistProblem}";

        if (add.Warning is not null) note = $"{note} {add.Warning}";

        return new PlaylistKeepResult(add.Outcome, add.Position, note, shortlistProblem is null);
    }

    /// <summary>
    /// The desktop's way in: builds its own OAuth writer and reads the tab names from appsettings,
    /// so the Mac's playlist window can call this with just the album.
    /// </summary>
    public static async Task<PlaylistKeepResult> KeepFromPlaylistAsync(
        PlaylistEntry album, Action<string>? progress = null)
    {
        var cfg = LoadSheetsConfig();
        var writer = await CreateWriterQuietlyAsync(cfg);
        if (writer is null)
            return new PlaylistKeepResult(AddOutcome.NotConfigured, null,
                "✗ Google Sheets isn't configured, so there's no replacements list to add to.", false);

        return await KeepFromPlaylistAsync(album, writer, cfg.ReplacementsTab, cfg.AlbumsTab,
                                           cfg.StarredTab, cacheLocally: true, progress);
    }

    /// <summary>
    /// Whether Apple Music already holds this album.
    /// <para>
    /// <see cref="PlaylistStore.SameAlbum"/> first — the usual rule, which allows for Apple Music
    /// naming an album by whichever edition it stocks. Then, failing that, the two names stripped of
    /// their trailing parentheticals and compared outright, because <em>both</em> sides put their own
    /// furniture on: the list writes "Led Zeppelin (self titled)" and Music stocks "Led Zeppelin
    /// (Remastered)", and the prefix rule can't see past either.
    /// </para>
    /// <para>
    /// This is not cosmetic — it is what makes the backfill converge. Without it an album the
    /// backfill had just favourited read as still missing on the next run, so every run redid the
    /// same work and pushed the same albums into the playlist again; Apple Music has no remove, so
    /// those duplicate tracks stay. It is the same blind spot the Music-app search had, at the other
    /// end of the same round trip (see MusicAppPlaylistWriter.SearchTerms).
    /// </para>
    /// <para>
    /// Equality on the bare names, not a prefix: over "Led Zeppelin" a prefix would count
    /// "Led Zeppelin II" as the self-titled debut. The artist, meanwhile, only has to
    /// <see cref="DiscogsApiClient.ArtistsOverlap">overlap</see> — the list and Apple Music disagree
    /// constantly about who made a record ("Ozzy Osborne" for Osbourne, "Simon And Garfunkel" for
    /// "Simon &amp;", "Dinosaur" for Dinosaur Jr., "Chicago Transit Authority" where Music says
    /// "Chicago"), and 22 albums the backfill had already dealt with read as missing on the next run
    /// for exactly that reason. It is the same leniency
    /// <see cref="AppleMusicCatalog.FindBestMatch"/> already applies at the other end, which is why
    /// those albums were found and added in the first place.
    /// </para>
    /// </summary>
    private static bool Holds(IReadOnlyList<PlaylistEntry> haystack, PlaylistEntry album)
    {
        if (haystack.Any(h => PlaylistStore.SameAlbum(h, album))) return true;

        string bare = DiscogsApiClient.BareTitle(album.Title);
        return haystack.Any(h => NumberedList.Matches(DiscogsApiClient.BareTitle(h.Title), bare)
                                 && DiscogsApiClient.ArtistsOverlap(h.Artist, album.Artist));
    }

    /// <summary>
    /// Whether an attempt to add really left the album on the replacements list. Only these two
    /// outcomes do: "already there" counts, because the album is where it was wanted. Everything
    /// else is a refusal, and the shortlist must not record a decision on the back of one — that
    /// would leave a row claiming a keep and pointing at a list it never joined.
    /// </summary>
    public static bool ReachedTheList(AddOutcome outcome) =>
        outcome is AddOutcome.Added or AddOutcome.AlreadyInReplacements;

    /// <summary>A four-digit year that isn't in the future or before recorded albums existed.</summary>
    private static bool TryYear(string? value, out int year) =>
        int.TryParse(value?.Trim(), out year) && year >= 1900 && year <= DateTime.Now.Year + 1;

    /// <summary>
    /// Writes "this one was kept" on to the shortlist, adding the album first if the shortlist has
    /// never heard of it — which is the usual case coming off a playlist. Returns null on success,
    /// or why it couldn't be saved.
    /// <para>
    /// Matched loosely, the way an intake matches, so an album already sitting on the shortlist
    /// under Apple Music's name for it is updated rather than duplicated. A candidate that was
    /// previously declined is flipped to kept — pressing Keep is a decision reversed on purpose,
    /// unlike an intake, which deliberately leaves a dropped album dropped.
    /// </para>
    /// </summary>
    private static async Task<string?> RecordKeptOnShortlistAsync(
        string title, string artist, int year, bool cacheLocally)
    {
        var repo = CandidateRepository.Create();
        if (!repo.SyncEnabled && !cacheLocally)
            return $"Sheets sync {repo.Status}, and this device has no local copy.";

        List<CandidateAlbum> shortlist;
        try { shortlist = await ShortlistIntake.LoadAsync(repo, cacheLocally); }
        catch (Exception ex) { return $"couldn't read it ({ex.Message})."; }

        var (_, match) = ReplacementCandidates.Classify(shortlist, c =>
            DiscogsApiClient.TitlesLineUp(c.Title, title) && NumberedList.Matches(c.Artist, artist));

        if (match is null)
        {
            shortlist.Add(new CandidateAlbum
            {
                Title = title,
                Artist = artist,
                Year = year.ToString(),
                Status = CandidateStatus.Added,
            });
        }
        else
        {
            match.Status = CandidateStatus.Added;
            if (match.Year.Trim().Length == 0) match.Year = year.ToString();
        }

        return await SaveShortlistAsync(repo, shortlist, cacheLocally);
    }

    /// <summary>Writes the shortlist back wherever it lives — local JSON and/or the Potentials sheet.</summary>
    private static async Task<string?> SaveShortlistAsync(
        CandidateRepository repo, List<CandidateAlbum> shortlist, bool cacheLocally)
    {
        string? problem = null;
        if (cacheLocally)
        {
            try { ReplacementCandidates.Save(shortlist); }
            catch (Exception ex) { problem = $"couldn't save it here ({ex.Message})."; }
        }
        if (repo.SyncEnabled)
        {
            try { await repo.PushAsync(shortlist); }
            catch (Exception ex) { problem = $"Google Sheets sync failed ({ex.Message})."; }
        }
        return problem;
    }

    /// <summary>
    /// The opposite of <see cref="KeepFromPlaylistAsync(PlaylistEntry, Action{string})"/>: an album
    /// taken off Playlist 2 after a listen is a recommendation turned down, so the shortlist marks it
    /// dropped — the shortlist's "Nah" — and it's never offered again, nor re-added by a later pull.
    /// An album not on the shortlist yet goes on as dropped, for the same reason. One already
    /// <em>kept</em> is left kept: removing a kept album from the listening queue just means done.
    /// Returns one line for the status area.
    /// </summary>
    public static async Task<string> DropFromShortlistAsync(PlaylistEntry album, bool cacheLocally = true)
    {
        string title = DiscogsApiClient.BareTitle(album.Title);
        var repo = CandidateRepository.Create();
        if (!repo.SyncEnabled && !cacheLocally)
            return $"⚠ The shortlist wasn't updated — Sheets sync {repo.Status}.";

        List<CandidateAlbum> shortlist;
        try { shortlist = await ShortlistIntake.LoadAsync(repo, cacheLocally); }
        catch (Exception ex) { return $"⚠ The shortlist wasn't updated — couldn't read it ({ex.Message})."; }

        var (_, match) = ReplacementCandidates.Classify(shortlist, c =>
            DiscogsApiClient.SameTitle(DiscogsApiClient.BareTitle(c.Title), title)
            && (NumberedList.Matches(c.Artist, album.Artist) || DiscogsApiClient.ArtistsOverlap(c.Artist, album.Artist)));

        if (match is { Status: CandidateStatus.Added }) return $"“{title}” stays kept.";
        if (match is { Status: CandidateStatus.Declined }) return $"“{title}” was already dropped from the shortlist.";

        if (match is null)
            shortlist.Add(new CandidateAlbum
            {
                Title = title, Artist = album.Artist, Year = album.Year, Status = CandidateStatus.Declined,
            });
        else
            match.Status = CandidateStatus.Declined;

        string? problem = await SaveShortlistAsync(repo, shortlist, cacheLocally);
        return problem is null
            ? $"Dropped “{title}” from the recommendations."
            : $"⚠ Couldn't drop “{title}” from the shortlist — {problem}";
    }

    // ---------- Apple Music favourites ----------

    /// <summary>One album off the lists, with what Apple Music is missing for it.</summary>
    public sealed record FavoritesBackfillItem(PlaylistEntry Album, bool NeedsFavorite, bool NeedsPlaylist)
    {
        public bool NeedsNothing => !NeedsFavorite && !NeedsPlaylist;

        /// <summary>What the backfill would do to this album, for a log line.</summary>
        public string Intent => NeedsNothing ? "nothing to do"
            : NeedsFavorite && NeedsPlaylist ? "favourite + playlist"
            : NeedsFavorite ? "favourite"
            : "playlist";
    }

    /// <summary>
    /// The whole finished list measured against Apple Music, decided before anything is written.
    /// Read-only — <see cref="PlanFavoritesBackfillAsync"/> produces it, the backfill acts on it, and
    /// the <c>favtest</c> rehearsal prints it and stops.
    /// </summary>
    /// <param name="FavoritedInLibrary">How many favourited albums Apple Music holds in total.</param>
    /// <param name="InPlaylist">How many albums the favourites playlist holds.</param>
    public sealed record FavoritesBackfillPlan(
        IReadOnlyList<FavoritesBackfillItem> Items,
        int FavoritedInLibrary,
        int InPlaylist,
        string? Error = null)
    {
        public static FavoritesBackfillPlan Failed(string error) =>
            new(Array.Empty<FavoritesBackfillItem>(), 0, 0, error);

        public int Considered => Items.Count;
        public int AlreadyFavorited => Items.Count(i => !i.NeedsFavorite);
        public int ToFavorite => Items.Count(i => i.NeedsFavorite);
        public int ToAddToPlaylist => Items.Count(i => i.NeedsPlaylist);

        /// <summary>Only the albums with something to do — what the backfill actually walks.</summary>
        public IReadOnlyList<FavoritesBackfillItem> Work =>
            Items.Where(i => !i.NeedsNothing).ToList();

        public string Summary =>
            $"{Considered} albums on the lists · {AlreadyFavorited} already favourited · "
            + $"{ToFavorite} to favourite · {ToAddToPlaylist} to add to “{AppleMusic.FavoritesPlaylistName}”";
    }

    /// <summary>
    /// Works out what the favourites backfill would have to do, writing nothing. Reads the two lists
    /// off Google Sheets, reads every favourited album and the favourites playlist out of Apple
    /// Music, and pairs them up.
    /// <para>
    /// The two halves are checked separately and independently. An album can perfectly well be
    /// favourited but missing from the playlist — that is what a hand-kept duplicate list drifts
    /// into — and each gap is decided on its own.
    /// </para>
    /// </summary>
    public static async Task<FavoritesBackfillPlan> PlanFavoritesBackfillAsync()
    {
        if (!AppleMusic.CanFavorite)
            return FavoritesBackfillPlan.Failed(AppleMusic.IsAvailable
                ? "This platform can add to a playlist but can't set favourites — see PROJECT.md §5."
                : "The Music app isn't available on this platform.");
        var writer = AppleMusic.Writer!;

        var lists = await LoadAllListsAsync();
        if (lists.Error is not null) return FavoritesBackfillPlan.Failed(lists.Error);

        // The finished list, in the order the PDF prints it: the kept ⭐ picks, then the additions.
        var wanted = lists.MustHear.Concat(lists.Replacements)
            .Select(r => new PlaylistEntry(r.Title, r.Artist, r.Year))
            .ToList();

        if (wanted.Count == 0)
            return FavoritesBackfillPlan.Failed("Both lists came back empty — nothing to backfill.");

        IReadOnlyList<PlaylistEntry> favorited, inPlaylist;
        try
        {
            favorited = await writer.ReadFavoritedAlbumsAsync();
            inPlaylist = await writer.ReadAlbumsAsync(AppleMusic.FavoritesPlaylistName);
        }
        catch (Exception ex)
        {
            return FavoritesBackfillPlan.Failed($"Couldn't read Apple Music: {ex.Message}");
        }

        var items = wanted
            .Select(a => new FavoritesBackfillItem(
                a,
                NeedsFavorite: !Holds(favorited, a),
                NeedsPlaylist: !Holds(inPlaylist, a)))
            .ToList();

        return new FavoritesBackfillPlan(items, favorited.Count, inPlaylist.Count);
    }

    /// <summary>What one run of the favourites backfill actually did.</summary>
    /// <param name="Favorited">Albums this run marked as favourites.</param>
    /// <param name="AddedToPlaylist">Albums this run put in the favourites playlist.</param>
    /// <param name="Failures">One line per album that couldn't be marked or added, and why.</param>
    public sealed record FavoritesBackfillResult(
        FavoritesBackfillPlan Plan, int Favorited, int AddedToPlaylist, IReadOnlyList<string> Failures)
    {
        public string Summary =>
            $"{Plan.Considered} albums on the lists · {Plan.AlreadyFavorited} already favourited · "
            + $"{Favorited} newly favourited · {AddedToPlaylist} added to “{AppleMusic.FavoritesPlaylistName}”"
            + (Failures.Count > 0 ? $" · {Failures.Count} couldn't be done" : "");
    }

    /// <summary>
    /// Puts a heart on every album of the finished list — the ⭐ picks kept from the 1001 plus the
    /// replacements — that Apple Music doesn't already have one on, and mirrors the same albums into
    /// the favourites playlist. A backfill: it works over a list that mostly already agrees with
    /// Apple Music, and touches only where the two differ.
    /// <para>
    /// Mac only. Apple's MediaPlayer framework on the phone can read a playlist and add to it but
    /// exposes no favourite at all — the same Swift-only MusicKit wall the missing remove API runs
    /// into (PROJECT.md §5) — so the phone says so rather than half-doing it.
    /// </para>
    /// <para>
    /// Idempotent, and safe to interrupt: it only ever sets a favourite on, never off, and it works
    /// from a plan read fresh from Apple Music each run, so a second run after an interrupted one
    /// carries on from where the first stopped.
    /// </para>
    /// </summary>
    /// <param name="progress">
    /// Called with a line per album. Each one is a Music-app library search and there are hundreds,
    /// so a run that says nothing for ten minutes is indistinguishable from one that has hung.
    /// </param>
    public static async Task<FavoritesBackfillResult?> BackfillAppleMusicFavoritesAsync(
        Action<string>? progress = null)
    {
        void Say(string line) { Console.WriteLine(line); progress?.Invoke(line); }

        Console.WriteLine("\n=== Backfill Apple Music favourites ===\n");
        Say("Reading the lists, and what Apple Music already has…");

        var plan = await PlanFavoritesBackfillAsync();
        if (plan.Error is not null) { Say($"✗ {plan.Error}"); return null; }

        Say($"Apple Music holds {plan.FavoritedInLibrary} favourited albums and {plan.InPlaylist} "
            + $"in “{AppleMusic.FavoritesPlaylistName}”. {plan.Summary}");

        var work = plan.Work;
        if (work.Count == 0)
        {
            Say("✓ Nothing to do — Apple Music already agrees with the lists.");
            return new FavoritesBackfillResult(plan, 0, 0, Array.Empty<string>());
        }

        int marked = 0, queued = 0;
        var failures = new List<string>();

        for (int i = 0; i < work.Count; i++)
        {
            var (album, needsFavorite, needsPlaylist) = work[i];
            Say($"[{i + 1}/{work.Count}] {album.Title} — {album.Artist} · {work[i].Intent}");

            // Favourite first: it also warms the writer's one-deep search memo, so the playlist add
            // that follows doesn't search the library for the same album all over again.
            if (needsFavorite)
            {
                var result = await AppleMusic.Writer!.FavoriteAlbumAsync(album);
                if (result.Ok) marked++;
                else failures.Add($"{album.Title} — {album.Artist}: {result.Message}");
            }

            if (needsPlaylist)
            {
                var result = await AppleMusic.Writer!.AddAlbumAsync(AppleMusic.FavoritesPlaylistName, album);
                if (result.Ok) queued++;
                else failures.Add($"{album.Title} — {album.Artist} (playlist): {result.Message}");
            }
        }

        var summary = new FavoritesBackfillResult(plan, marked, queued, failures);
        Say($"✓ {summary.Summary}");
        foreach (var failure in failures) Say($"   ✗ {failure}");
        return summary;
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

        return await AddReplacementAlbumAsync(
            writer, cfg.ReplacementsTab, cfg.AlbumsTab, cfg.StarredTab, title, artist, year);
    }

    /// <summary>
    /// <inheritdoc cref="AddReplacementAlbumAsync(string, string, int)"/>
    /// <para>
    /// Takes the client and tab names outright so the phone can call it too: mobile reaches the same
    /// spreadsheet over REST (<see cref="RestSheetsClient"/>) rather than the desktop's OAuth writer,
    /// and reads its config from the embedded copy — see <see cref="EmbeddedConfig"/>.
    /// </para>
    /// </summary>
    public static async Task<AddResult> AddReplacementAlbumAsync(
        ISheetsClient client, string replacementsTab, string albumsTab, string starredTab,
        string title, string artist, int year)
    {
        try
        {
            // 1. Already a replacement?
            var replacements = await NumberedList.ReadAsync(client, replacementsTab);
            if (NumberedList.Find(replacements, title, artist) is { } dupe)
            {
                string detail = $"Already on your replacements list at #{dupe.Number} " +
                                $"— “{dupe.Title}” by {dupe.Artist} ({dupe.Year}).";
                Console.WriteLine($"⚠️  {detail}");
                return new AddResult(AddOutcome.AlreadyInReplacements, null, detail);
            }

            // 2. Already on the canonical 1001 list?
            var master = await RatingSession.LoadAsync(client, albumsTab, starredTab);
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
                client, replacementsTab, title, artist, year);
            Console.WriteLine($"✓ Added “{title}” ({year}) to \"{replacementsTab}\" at #{position}.");
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
