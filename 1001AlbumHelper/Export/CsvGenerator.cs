namespace _1001AlbumHelper;

public class CsvGenerator
{
    public void CreateAlbumSpreadsheet(List<Album> albums, string fileName)
    {
        try
        {
            Directory.CreateDirectory(Operations.OutputDir);
            string outputPath = Path.Combine(Operations.OutputDir, fileName);

            using var writer = new StreamWriter(outputPath);

            // Write legend (matching Google Sheets format exactly)
            writer.WriteLine("0,⭐,Really enjoyable/Favs,Only bangers and/or perfect albums.");
            writer.WriteLine("0,👍,Mostly enjoyable,\"Good stuff, but just not my favorite, or just not a perfect album.\"");
            writer.WriteLine("0,👎,Mostly sucks,Probably means the singer is bad.");
            writer.WriteLine("0,❌,Trash,Actual torture to get through.");
            writer.WriteLine();
            
            // Write headers
            writer.WriteLine("#,Rating,Album,Artist,Year");
            
            // Write data
            for (int i = 0; i < albums.Count; i++)
            {
                var album = albums[i];
                writer.WriteLine($"\"{i + 1}\",\"\",\"{EscapeCsv(album.AlbumName)}\",\"{EscapeCsv(album.Artist)}\",\"{album.Year}\"");
            }

            Console.WriteLine($"Successfully created CSV file with {albums.Count} albums!");
            Console.WriteLine($"Saved to: {outputPath}");
            Console.WriteLine("\nYou can now:");
            Console.WriteLine("1. Upload this CSV to Google Sheets");
            Console.WriteLine("2. Add ⭐ in the Rating column for albums you love");
            Console.WriteLine("3. Download as Excel (.xlsx) and save to output folder");
            Console.WriteLine("4. Run Option 2 to create your starred albums list");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating CSV file: {ex.Message}");
        }
    }

    public void CreateStarredAlbumsList(List<(string Album, string Artist, string Year)> albums, string fileName)
    {
        try
        {
            Directory.CreateDirectory(Operations.OutputDir);
            string outputPath = Path.Combine(Operations.OutputDir, fileName);

            using var writer = new StreamWriter(outputPath);

            // Write headers
            writer.WriteLine("#,Album,Artist,Year");
            
            // Write data
            for (int i = 0; i < albums.Count; i++)
            {
                var album = albums[i];
                writer.WriteLine($"\"{i + 1}\",\"{EscapeCsv(album.Album)}\",\"{EscapeCsv(album.Artist)}\",\"{album.Year}\"");
            }

            Console.WriteLine($"Successfully created starred albums CSV with {albums.Count} albums!");
            Console.WriteLine($"Saved to: {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating CSV file: {ex.Message}");
        }
    }

    private string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        
        // Replace quotes with double quotes for CSV escaping
        return value.Replace("\"", "\"\"");
    }
}
