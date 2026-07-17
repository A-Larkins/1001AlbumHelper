namespace _1001AlbumHelper;

/// <summary>
/// The five core helper operations, shared by both the console menu and the web UI.
/// Every operation writes its progress to <see cref="Console"/> so callers can capture
/// it however they like (the console prints it directly; the web UI streams it to the browser).
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
}
