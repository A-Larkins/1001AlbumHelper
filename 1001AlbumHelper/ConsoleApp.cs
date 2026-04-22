using _1001AlbumHelper;

bool keepRunning = true;

while (keepRunning)
{
    Console.WriteLine("\n=== 1001 Albums Helper ===\n");

    Console.WriteLine("Common options:");
    Console.WriteLine("  1. Download latest sheets from Google Sheets");
    Console.WriteLine("     Download the newest versions of all your album lists from Google Sheets into the input folder.\n");
    Console.WriteLine("  2. Create 'Albums Larkins Thinks You Must Hear' from starred albums");
    Console.WriteLine("     Generate a list of your favorite albums (those you've starred) from your main rated CSV.\n");
    Console.WriteLine("  3. Renumber My Replacement Albums list");
    Console.WriteLine("     Sort and renumber your replacement albums list, saving a clean version to the output folder.\n");

    Console.WriteLine("Other options:");
    Console.WriteLine("  4. Fetch fresh 1001 Albums list from Discogs");
    Console.WriteLine("     Download the official 1001 albums list from Discogs and save as a CSV.\n");
    Console.WriteLine("  5. Merge Discogs list with my existing ratings");
    Console.WriteLine("     Combine the Discogs album list with your ratings to create a merged CSV.\n");
    Console.WriteLine("Enter your choice (1-5, or 'q' to quit): ");

    string? choice = Console.ReadLine();

    switch (choice?.ToLower())
    {
        case "1":
            await DownloadGoogleSheetsAsync();
            break;
        case "2":
            CreateStarredAlbumsList();
            break;
        case "3":
            RenumberReplacementAlbums();
            break;
        case "4":
            await FetchFreshListAsync();
            break;
        case "5":
            MergeRatingsWithDiscogsList();
            break;
        case "q":
            Console.WriteLine("\nGoodbye!");
            keepRunning = false;
            break;
        default:
            Console.WriteLine("Invalid choice. Try again.");
            break;
    }
}

async Task FetchFreshListAsync()
{
    Console.WriteLine("\n=== Fetching 1001 Albums from Discogs ===\n");
    Console.WriteLine("Fetching albums from Discogs API...");
    
    var apiClient = new DiscogsApiClient();
    var albums = await apiClient.FetchAlbumsFromListAsync("991847");

    if (albums.Count > 0)
    {
        Console.WriteLine($"\nFound {albums.Count} albums. Creating CSV file...");
        var csvGenerator = new CsvGenerator();
        csvGenerator.CreateAlbumSpreadsheet(albums, "1001Albums.csv");
    }
    else
    {
        Console.WriteLine("No albums found.");
    }
}

void CreateStarredAlbumsList()
{
    Console.WriteLine("\n=== Creating Starred Albums List ===\n");
    var processor = new AlbumProcessor();
    string inputPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "input", "1001 Albums You Must Hear Before You Die - 1001 albums.csv");
    inputPath = Path.GetFullPath(inputPath);
    string outputFileName = "1001 Albums Larkins Thinks You Must Hear.csv";
    processor.CreateStarredAlbumsList(inputPath, outputFileName);

    // Open the generated file
    string outputPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "output", outputFileName);
    outputPath = Path.GetFullPath(outputPath);
    TryOpenFile(outputPath);
}

void RenumberReplacementAlbums()
{
    Console.WriteLine("\n=== Renumbering Replacement Albums ===\n");
    var processor = new AlbumProcessor();
    string inputPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "input", "1001 Albums You Must Hear Before You Die - *my replacement albums.csv");
    inputPath = Path.GetFullPath(inputPath);
    string outputFileName = "my replacement albums.csv";
    processor.RenumberReplacementAlbums(inputPath, outputFileName);

    // Open the generated file
    string outputPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "output", outputFileName);
    outputPath = Path.GetFullPath(outputPath);
    TryOpenFile(outputPath);
}

// Helper to open a file in the default app (cross-platform)
void TryOpenFile(string filePath)
{
    try
    {
#if WINDOWS
    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
#elif MACOS
    System.Diagnostics.Process.Start("open", $"\"{filePath}\"");
#elif LINUX
    System.Diagnostics.Process.Start("xdg-open", $"\"{filePath}\"");
#else
    // Try open for .NET 6+
    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = filePath, UseShellExecute = true });
#endif
    }
    catch (Exception ex)
    {
    Console.WriteLine($"(Could not open file automatically: {ex.Message})");
    }
}

async Task DownloadGoogleSheetsAsync()
{
    Console.WriteLine("\n=== Downloading from Google Sheets ===\n");
    
    string spreadsheetId = "1UKN0bBNM3Hr5QaggiPcYUSRSt84o3dI0_06k_kIkY7k";
    
    // Sheet GIDs from the Google Sheets tabs
    var sheets = new[]
    {
        (gid: "0", name: "1001 Albums You Must Hear Before You Die - 1001 albums.csv"),
        (gid: "729317458", name: "1001 Albums You Must Hear Before You Die - 1001 Albums Larkins Thinks You Must Hear.csv"),
        (gid: "1918952882", name: "1001 Albums You Must Hear Before You Die - *my replacement albums.csv")
    };
    
    using var client = new HttpClient();
    Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "input"));
    
    foreach (var sheet in sheets)
    {
        try
        {
            string url = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=csv&gid={sheet.gid}";
            Console.WriteLine($"Downloading {sheet.name}...");
            
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            string content = await response.Content.ReadAsStringAsync();
            string outputPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "input", sheet.name);
            outputPath = Path.GetFullPath(outputPath);
            
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

void MergeRatingsWithDiscogsList()
{
    Console.WriteLine("\n=== Merging Ratings with Discogs List ===\n");
    
    var processor = new AlbumProcessor();
    
    // Path to the fresh Discogs list (from Option 1)
    string discogsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "output", "1001Albums.csv");
    discogsPath = Path.GetFullPath(discogsPath);
    
    // Path to your existing rated list (from Google Sheets / input folder)
    string ratedPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "input", "1001 Albums You Must Hear Before You Die - 1001 albums.csv");
    ratedPath = Path.GetFullPath(ratedPath);
    
    processor.MergeRatingsWithDiscogsList(discogsPath, ratedPath, "1001Albums.csv");
}

