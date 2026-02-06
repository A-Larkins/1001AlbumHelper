using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace _1001AlbumHelper;

public class AlbumProcessor
{
    public void CreateStarredAlbumsList(string inputPath, string outputFileName)
    {
        try
        {
            if (!File.Exists(inputPath))
            {
                Console.WriteLine($"Error: Input file not found at {inputPath}");
                Console.WriteLine("Please place your rated CSV file in the input folder.");
                return;
            }

            Console.WriteLine("Reading CSV file...");
            
            var starredAlbums = new List<(string Album, string Artist, string Year)>();
            
            // Read CSV file
            var lines = File.ReadAllLines(inputPath);
            int rowsProcessed = 0;
            
            foreach (var line in lines)
            {
                // Skip empty lines
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                // Skip header/legend lines
                if (line.StartsWith("0,") || line.Contains("Rating,Album,Artist") || line.Contains("Really enjoyable"))
                    continue;
                
                // Parse CSV line
                var parts = ParseCsvLine(line);
                if (parts.Length < 5) continue;
                
                string number = parts[0];
                string rating = parts[1];
                string album = parts[2];
                string artist = parts[3];
                string year = parts[4];
                
                // Skip if no album name
                if (string.IsNullOrWhiteSpace(album)) continue;
                
                rowsProcessed++;
                
                // Debug: show first few ratings
                if (rowsProcessed <= 5 || !string.IsNullOrWhiteSpace(rating))
                {
                    Console.WriteLine($"Row {number}: Rating='{rating}' (length={rating.Length}), Album='{album}'");
                }
                
                // Check if the rating contains a star emoji
                if (rating.Contains("⭐") || rating.Contains("🌟") || rating.Contains("⭐️"))
                {
                    starredAlbums.Add((album, artist, year));
                }
            }
            
            Console.WriteLine($"\nProcessed {rowsProcessed} rows total.");

            if (starredAlbums.Count == 0)
            {
                Console.WriteLine("No starred albums found in the file.");
                return;
            }

            // Create CSV output
            var csvGenerator = new CsvGenerator();
            csvGenerator.CreateStarredAlbumsList(starredAlbums, outputFileName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing starred albums: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }

    private string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var currentField = new System.Text.StringBuilder();
        bool inQuotes = false;
        
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Escaped quote
                    currentField.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }
        
        result.Add(currentField.ToString());
        return result.ToArray();
    }

    public void RenumberReplacementAlbums(string inputPath, string outputFileName)
    {
        try
        {
            if (!File.Exists(inputPath))
            {
                Console.WriteLine($"Error: Input file not found at {inputPath}");
                Console.WriteLine("Please place your replacement albums CSV file in the input folder.");
                return;
            }

            Console.WriteLine("Reading replacement albums CSV...");
            var albums = new List<(string Artist, string Album, string Year)>();

            // Read CSV file
            var lines = File.ReadAllLines(inputPath);
            
            foreach (var line in lines)
            {
                // Skip empty lines
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                // Parse CSV line
                var parts = ParseCsvLine(line);
                if (parts.Length < 3) continue;
                
                // Check if this looks like a header or data row
                if (parts[0] == "#" || parts[0] == "Artist" || parts[1] == "Artist") continue;
                
                // Determine column order - could be Artist,Album,Year or #,Artist,Album,Year
                string artist, album, year;
                if (parts.Length == 3)
                {
                    artist = parts[0];
                    album = parts[1];
                    year = parts[2];
                }
                else
                {
                    // Has number column, skip it
                    artist = parts[1];
                    album = parts[2];
                    year = parts.Length > 3 ? parts[3] : "";
                }
                
                if (!string.IsNullOrWhiteSpace(album))
                {
                    albums.Add((artist, album, year));
                }
            }

            if (albums.Count == 0)
            {
                Console.WriteLine("No albums found in the replacement sheet.");
                return;
            }

            // Sort by year
            Console.WriteLine("Sorting albums by year...");
            albums = albums.OrderBy(a => 
            {
                if (int.TryParse(a.Year, out int year))
                    return year;
                return 9999; // Put non-numeric years at the end
            }).ToList();

            // Create CSV output
            Directory.CreateDirectory("output");
            string outputPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "output", outputFileName);
            outputPath = Path.GetFullPath(outputPath);

            using var writer = new StreamWriter(outputPath);
            
            // Write headers
            writer.WriteLine("#,Artist,Album,Year");
            
            // Write data with numbering
            for (int i = 0; i < albums.Count; i++)
            {
                string artist = albums[i].Artist.Replace("\"", "\"\"");
                string album = albums[i].Album.Replace("\"", "\"\"");
                writer.WriteLine($"{i + 1},\"{artist}\",\"{album}\",{albums[i].Year}");
            }

            Console.WriteLine($"Successfully renumbered {albums.Count} replacement albums!");
            Console.WriteLine($"Saved to: {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing replacement albums: {ex.Message}");
        }
    }

    private string GetCellValue(Cell? cell, SharedStringTable? stringTable)
    {
        if (cell?.CellValue == null)
            return string.Empty;

        string value = cell.CellValue.Text;
        
        // If it's a shared string, look it up in the string table
        if (cell.DataType?.Value == CellValues.SharedString && stringTable != null)
        {
            return stringTable.Elements<SharedStringItem>().ElementAt(int.Parse(value)).InnerText;
        }
        
        return value;
    }

    public void MergeRatingsWithDiscogsList(string discogsPath, string ratedPath, string outputFileName)
    {
        // ⭐	Really enjoyable/Favs	Only bangers and/or perfect albums.
        // 👍	Mostly enjoyable	Good stuff, but just not my favorite, or just not a perfect album.
        // 👎	Mostly sucks	Probably means the singer is bad.
        // ❌	Trash	Actual torture to get through.
        
        try
        {
            if (!File.Exists(discogsPath))
            {
                Console.WriteLine($"Error: Discogs file not found at {discogsPath}");
                Console.WriteLine("Please run Option 1 first to fetch the Discogs list.");
                return;
            }

            if (!File.Exists(ratedPath))
            {
                Console.WriteLine($"Error: Rated file not found at {ratedPath}");
                Console.WriteLine("Please run Option 4 first to download your Google Sheets.");
                return;
            }

            Console.WriteLine("Reading your existing ratings...");
            
            // Known album/artist aliases that Discogs uses differently
            var albumAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "untitled", "led zeppelin iv (four symbols)" },
                { "led zeppelin iv", "led zeppelin iv (four symbols)" },
                { "david byrne", "my life in the bush of ghosts" }
            };
            
            var artistAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "scott engel", "scott walker" },
                { "brian eno", "brian eno & david byrne" } // for My Life in the Bush of Ghosts
            };
            
            // Read rated albums into a dictionary (Album+Artist as key, Rating as value)
            var ratings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var ratedLines = File.ReadAllLines(ratedPath);
            
            foreach (var line in ratedLines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("0,") || line.Contains("Really enjoyable")) continue;
                
                var parts = ParseCsvLine(line);
                if (parts.Length < 5) continue;
                
                string rating = parts[1];
                string album = parts[2];
                string artist = parts[3];
                
                if (string.IsNullOrWhiteSpace(album)) continue;
                
                // Create a normalized key for matching
                string key = $"{album.Trim().ToLower()}|||{artist.Trim().ToLower()}";
                if (!string.IsNullOrWhiteSpace(rating))
                {
                    ratings[key] = rating;
                }
            }
            
            Console.WriteLine($"Found {ratings.Count} rated albums in your list.");
            Console.WriteLine("\nReading Discogs list...");
            
            // Read Discogs CSV
            var discogsLines = File.ReadAllLines(discogsPath);
            var mergedLines = new List<string>();
            int matchedCount = 0;
            
            foreach (var line in discogsLines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    mergedLines.Add(line);
                    continue;
                }
                
                // Keep legend lines as-is
                if (line.StartsWith("\"0\",") || line.Contains("Rating Legend") || line.Contains("Must Hear"))
                {
                    mergedLines.Add(line);
                    continue;
                }
                
                var parts = ParseCsvLine(line);
                if (parts.Length < 5)
                {
                    mergedLines.Add(line);
                    continue;
                }
                
                string number = parts[0];
                string currentRating = parts[1];
                string album = parts[2];
                string artist = parts[3];
                string year = parts[4];
                
                // Try exact match first
                string exactKey = $"{album.Trim().ToLower()}|||{artist.Trim().ToLower()}";
                if (ratings.TryGetValue(exactKey, out string? foundRating))
                {
                    mergedLines.Add($"\"{number}\",\"{foundRating}\",\"{album}\",\"{artist}\",\"{year}\"");
                    matchedCount++;
                    continue;
                }
                
                // Apply known aliases to Discogs data and try again
                string albumLookup = album.Trim().ToLower();
                if (albumAliases.ContainsKey(albumLookup))
                {
                    albumLookup = albumAliases[albumLookup];
                }
                
                string artistLookup = artist.Trim().ToLower();
                if (artistAliases.ContainsKey(artistLookup))
                {
                    artistLookup = artistAliases[artistLookup];
                }
                
                // Try with aliases
                string aliasKey = $"{albumLookup}|||{artistLookup}";
                if (aliasKey != exactKey && ratings.TryGetValue(aliasKey, out foundRating))
                {
                    mergedLines.Add($"\"{number}\",\"{foundRating}\",\"{album}\",\"{artist}\",\"{year}\"");
                    matchedCount++;
                    continue;
                }
                
                // Try fuzzy match - normalize album and artist names
                string normalizedAlbum = NormalizeAlbumName(album);
                string normalizedArtist = NormalizeArtistName(artist);
                
                // Search through all ratings for a fuzzy match
                bool found = false;
                foreach (var kvp in ratings)
                {
                    var keyParts = kvp.Key.Split("|||");
                    string ratedAlbum = NormalizeAlbumName(keyParts[0]);
                    string ratedArtist = NormalizeArtistName(keyParts[1]);
                    
                    // Match if album names are similar AND artist matches
                    if (AlbumNamesMatch(normalizedAlbum, ratedAlbum) && ArtistNamesMatch(normalizedArtist, ratedArtist))
                    {
                        mergedLines.Add($"\"{number}\",\"{kvp.Value}\",\"{album}\",\"{artist}\",\"{year}\"");
                        matchedCount++;
                        found = true;
                        break;
                    }
                }
                
                if (!found)
                {
                    // No match, keep the line as-is (empty rating)
                    mergedLines.Add(line);
                }
            }
            
            Console.WriteLine($"Matched {matchedCount} albums with your existing ratings.");
            Console.WriteLine("\nSaving merged list...");
            
            // Save output
            Directory.CreateDirectory("output");
            string outputPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "output", outputFileName);
            outputPath = Path.GetFullPath(outputPath);
            
            File.WriteAllLines(outputPath, mergedLines);
            
            Console.WriteLine($"✓ Successfully merged! Saved to: {outputPath}");
            Console.WriteLine($"\nSummary:");
            Console.WriteLine($"  - Total albums: {mergedLines.Count(l => !string.IsNullOrWhiteSpace(l) && !l.Contains("Legend") && !l.StartsWith("\"0\""))}");
            Console.WriteLine($"  - Rated albums: {matchedCount}");
            Console.WriteLine($"  - Unrated albums: {mergedLines.Count(l => !string.IsNullOrWhiteSpace(l) && !l.Contains("Legend") && !l.StartsWith("\"0\"")) - matchedCount}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error merging ratings: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }

    private string NormalizeAlbumName(string album)
    {
        // Remove punctuation, quotes, extra spaces, make lowercase
        var normalized = album.ToLower()
            .Replace("!", "")
            .Replace("?", "")
            .Replace(".", "")
            .Replace(",", "")
            .Replace("'", "")
            .Replace("\"", "")
            .Replace(":", "")
            .Replace(";", "")
            .Replace("-", " ")
            .Replace("/", "")
            .Replace("  ", " ")
            .Replace("(self titled)", "")
            .Replace("(selftitled)", "")
            .Trim();
        
        // Remove common prefixes that might differ
        var prefixes = new[] { "live at ", "live ", "at " };
        foreach (var prefix in prefixes)
        {
            if (normalized.StartsWith(prefix))
            {
                normalized = normalized.Substring(prefix.Length).Trim();
            }
        }
        
        return normalized.Replace("  ", " ").Trim();
    }

    private string NormalizeArtistName(string artist)
    {
        // Extract core artist name - remove "featuring", "with", "and the", etc.
        var normalized = artist.ToLower().Trim();
        
        // Remove periods (Mr. -> Mr)
        normalized = normalized.Replace(".", "");
        
        // Split on common separators
        var separators = new[] { " featuring ", " feat ", " with ", " & ", " and " };
        foreach (var sep in separators)
        {
            var idx = normalized.IndexOf(sep);
            if (idx > 0)
            {
                normalized = normalized.Substring(0, idx).Trim();
                break;
            }
        }
        
        return normalized;
    }

    private bool AlbumNamesMatch(string album1, string album2)
    {
        // Check if one album name contains the other (handles "This Is Fats" vs "This Is Fats Domino!")
        return album1.Contains(album2) || album2.Contains(album1);
    }

    private bool ArtistNamesMatch(string artist1, string artist2)
    {
        // Check if one artist name contains the other (handles "Louis Prima" vs "Louis Prima Featuring...")
        return artist1.Contains(artist2) || artist2.Contains(artist1);
    }
}
