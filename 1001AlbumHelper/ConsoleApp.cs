using _1001AlbumHelper;

bool keepRunning = true;

while (keepRunning)
{
    Console.WriteLine("\n=== 1001 Albums Helper ===\n");
    Console.WriteLine("1. Fetch fresh 1001 Albums list from Discogs");
    Console.WriteLine("2. Create 'Albums Larkins Thinks You Must Hear' from starred albums");
    Console.WriteLine("3. Renumber My Replacement Albums list");
    Console.WriteLine("4. Download latest sheets from Google Sheets");
    Console.WriteLine("5. Merge Discogs list with my existing ratings");
    Console.WriteLine("\nEnter your choice (1-5, or 'q' to quit): ");

    string? choice = Console.ReadLine();

    switch (choice?.ToLower())
    {
        case "1":
            await FetchFreshListAsync();
            break;
        case "2":
            CreateStarredAlbumsList();
            break;
        case "3":
            RenumberReplacementAlbums();
            break;
        case "4":
            await DownloadGoogleSheetsAsync();
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
    // Read the main file with ratings, output filtered starred albums
    string inputPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "input", "1001 Albums You Must Hear Before You Die - 1001 albums.csv");
    inputPath = Path.GetFullPath(inputPath);
    processor.CreateStarredAlbumsList(inputPath, "1001 Albums Larkins Thinks You Must Hear.csv");
}

void RenumberReplacementAlbums()
{
    Console.WriteLine("\n=== Renumbering Replacement Albums ===\n");
    
    var processor = new AlbumProcessor();
    // Read the replacement albums CSV and renumber it
    string inputPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "input", "1001 Albums You Must Hear Before You Die - *my replacement albums.csv");
    inputPath = Path.GetFullPath(inputPath);
    processor.RenumberReplacementAlbums(inputPath, "my replacement albums.csv");
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

