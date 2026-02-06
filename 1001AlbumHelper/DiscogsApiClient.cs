using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace _1001AlbumHelper;

public class DiscogsApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _token;

    public DiscogsApiClient()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "1001AlbumHelper/1.0 (https://github.com/alarks/1001AlbumHelper)");
        
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
            
        _token = config["Discogs:Token"] ?? "";
        if (string.IsNullOrEmpty(_token))
        {
            throw new Exception("Discogs token not found in appsettings.json");
        }
    }

    public async Task<List<Album>> FetchAlbumsFromListAsync(string listId)
    {
        var albums = new List<Album>();

        try
        {
            string url = $"https://api.discogs.com/lists/{listId}?token={_token}";
            Console.WriteLine("Fetching list from Discogs API...");

            var response = await _httpClient.GetStringAsync(url);
            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            List<JsonElement> items = new List<JsonElement>();
            items = root.GetProperty("items").EnumerateArray().ToList();
            
            List<string> ids = items.Select(item => item.GetProperty("id").GetInt32().ToString()).ToList();     

            Console.WriteLine($"Fetching {ids.Count} releases (60 requests/min with auth)...");
            int estimatedMinutes = (int)Math.Ceiling(ids.Count / 60.0);
            Console.WriteLine($"⚠️  This will take approximately {estimatedMinutes} minutes to complete.\n");

            int count = 0;
            foreach(string id in ids)
            {
                count++;
                string masterUrl = $"https://api.discogs.com/masters/{id}?token={_token}";
                var masterResponse = await _httpClient.GetStringAsync(masterUrl);
                var masterDoc = JsonDocument.Parse(masterResponse);
                var masterRoot = masterDoc.RootElement;

                string year = "";
                if (masterRoot.TryGetProperty("year", out JsonElement yearEl) && yearEl.GetInt32() > 0)
                {
                    year = yearEl.GetInt32().ToString();
                }

                if (count % 50 == 0)
                    Console.WriteLine($"Progress: {count}/{ids.Count}");

                // 60 req/min = 1 per second
                await Task.Delay(1000);

                foreach (var item in items)
                {
                    int itemId = item.GetProperty("id").GetInt32();
                    if (itemId.ToString() == id)
                    {
                        string artist = item.GetProperty("display_title").GetString()?.Split(" - ")[0].TrimEnd('*') ?? "";
                        string albumName = item.GetProperty("display_title").GetString()?.Split(" - ")[1] ?? "";

                        // Handle self-titled albums
                        if (!string.IsNullOrEmpty(artist) && albumName.Equals(artist, StringComparison.OrdinalIgnoreCase))
                        {
                            if (artist.Equals("The Beatles", StringComparison.OrdinalIgnoreCase))
                                albumName = "The Beatles (White Album)";
                            else if (artist.Equals("Metallica", StringComparison.OrdinalIgnoreCase))
                                albumName = "Metallica (Black Album)";
                            else
                                albumName = $"{albumName} (self titled)";
                        }

                        Album album = new Album
                        {
                            AlbumName = albumName,
                            Artist = artist,
                            Year = year
                        };

                        albums.Add(album);
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Successfully fetched {albums.Count} albums!");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Error fetching from Discogs API: {ex.Message}");
            Console.WriteLine("Make sure you have internet connection.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        return albums;
    }
}
