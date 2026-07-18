namespace _1001AlbumHelper;

/// <summary>The classic interactive text menu. Run with `dotnet run -- console`.</summary>
public static class ConsoleMenu
{
    public static async Task RunAsync()
    {
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

            Console.WriteLine("Google Sheets sync:");
            Console.WriteLine("  6. Build & sync BOTH lists to Google Sheets (one click)");
            Console.WriteLine("     Rebuild the starred list, renumber replacements, and write both into your sheet.\n");
            Console.WriteLine("  7. Build & sync 'Must Hear' list to Google Sheets\n");
            Console.WriteLine("  8. Renumber & sync replacement albums to Google Sheets\n");
            Console.WriteLine("  9. Check Google Sheets connection (list tabs)\n");
            Console.WriteLine("Enter your choice (1-9, or 'q' to quit): ");

            string? choice = Console.ReadLine();

            switch (choice?.ToLower())
            {
                case "1":
                    await Operations.DownloadGoogleSheetsAsync();
                    break;
                case "2":
                    Operations.CreateStarredAlbumsList();
                    TryOpenFile(Path.Combine(Operations.OutputDir, Operations.StarredOutputFile));
                    break;
                case "3":
                    Operations.RenumberReplacementAlbums();
                    TryOpenFile(Path.Combine(Operations.OutputDir, Operations.ReplacementOutputFile));
                    break;
                case "4":
                    await Operations.FetchFreshListAsync();
                    break;
                case "5":
                    Operations.MergeRatingsWithDiscogsList();
                    break;
                case "6":
                    await Operations.SyncBothToSheetsAsync();
                    break;
                case "7":
                    await Operations.SyncStarredToSheetAsync();
                    break;
                case "8":
                    await Operations.SyncReplacementsToSheetAsync();
                    break;
                case "9":
                    await Operations.ListSheetTabsAsync();
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
    }

    // Helper to open a file in the default app (cross-platform)
    private static void TryOpenFile(string filePath)
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
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = filePath, UseShellExecute = true });
#endif
        }
        catch (Exception ex)
        {
            Console.WriteLine($"(Could not open file automatically: {ex.Message})");
        }
    }
}
