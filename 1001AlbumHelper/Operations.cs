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

    /// <summary>Download the official 1001-albums list from Discogs and save it as a CSV.</summary>
    public static async Task FetchFreshListAsync()
    {
        Console.WriteLine("\n=== Fetching 1001 Albums from Discogs ===\n");
        Console.WriteLine("Fetching albums from Discogs API...");

        var apiClient = new DiscogsApiClient();
        var albums = await apiClient.FetchAlbumsFromListAsync("991847");

        if (albums.Count > 0)
        {
            Console.WriteLine($"\nFound {albums.Count} albums. Creating CSV file...");
            var csvGenerator = new CsvGenerator();
            csvGenerator.CreateAlbumSpreadsheet(albums, DiscogsOutputFile);
        }
        else
        {
            Console.WriteLine("No albums found.");
        }
    }

    /// <summary>Combine the Discogs list with your existing ratings into a merged CSV.</summary>
    public static void MergeRatingsWithDiscogsList()
    {
        Console.WriteLine("\n=== Merging Ratings with Discogs List ===\n");

        var processor = new AlbumProcessor();

        // The fresh Discogs list produced by FetchFreshListAsync
        string discogsPath = Path.Combine(OutputDir, DiscogsOutputFile);

        // Your existing rated list downloaded from Google Sheets
        string ratedPath = Path.Combine(InputDir, RatedListFile);

        processor.MergeRatingsWithDiscogsList(discogsPath, ratedPath, DiscogsOutputFile);
    }

    // ----- Google Sheets sync -------------------------------------------------

    /// <summary>Build the starred list and the renumbered replacements, then push both to Google Sheets.</summary>
    public static async Task SyncBothToSheetsAsync()
    {
        Console.WriteLine("\n=== Build & sync BOTH lists to Google Sheets ===\n");

        var cfg = LoadSheetsConfig();
        var writer = await CreateWriterAsync(cfg);
        if (writer is null) return;

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

        RenumberReplacementAlbums();
        await PushToSheetAsync(writer, ReplacementOutputFile, cfg.ReplacementsTab);
        Console.WriteLine("\n✓ Synced to Google Sheets.");
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
            CredentialsFile: config["GoogleSheets:CredentialsFile"] ?? "credentials.json");
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
        string SpreadsheetId, string StarredTab, string ReplacementsTab, string CredentialsFile);
}
