using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;

namespace _1001AlbumHelper;

/// <summary>
/// Cover art for an album, found through the same public iTunes catalog lookup the playlist export
/// uses (<see cref="AppleMusicCatalog"/>) — so the art shown is the art of the album we'd actually
/// add to a playlist, deluxe-edition tie-breaks and all.
/// <para>
/// Two layers of cache sit in front of that lookup, because the iTunes Search API is rate-limited
/// (roughly 20 calls a minute) and rating a run of albums would otherwise burn through it: a
/// per-run in-memory cache of decoded bitmaps, and a permanent on-disk cache of the downloaded
/// JPEGs in the app's writable folder. Only the very first sighting of an album costs a lookup.
/// </para>
/// <para>
/// Nothing here is load-bearing: every failure path returns null and the caller just shows no art.
/// </para>
/// </summary>
public static class AlbumArtwork
{
    /// <summary>Art is fetched at this size — big enough for a retina rating card, small enough to cache.</summary>
    private const int Pixels = 600;

    /// <summary>
    /// How many decoded bitmaps to hold at once. Each 600px cover is ~1.4 MB in memory, so this is
    /// a deliberate ceiling rather than a target — the disk cache makes re-decoding cheap.
    /// </summary>
    private const int MaxCachedImages = 40;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly ConcurrentDictionary<string, Task<Fetched>> Cache = new();

    /// <summary>A finished lookup. <paramref name="Retry"/> separates "it failed" from "there is none".</summary>
    private sealed record Fetched(Bitmap? Image, bool Retry);

    /// <summary>
    /// The album's cover art, or null if there isn't any (or the lookup failed). Safe to call
    /// repeatedly for the same album — concurrent callers share one lookup.
    /// </summary>
    public static async Task<Bitmap?> LoadAsync(string artist, string title, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title)) return null;

        string key = $"{artist}|{title}".ToLowerInvariant();

        // The shared task deliberately isn't tied to any one caller's cancellation token — the user
        // moving to the next album must not cancel a fetch another caller is still waiting on, and
        // a half-cancelled task must never be left in the cache. Callers wait with their own token.
        var task = Cache.GetOrAdd(key, _ => FetchAsync(artist, title));

        Fetched result;
        try
        {
            result = await task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        // A network failure shouldn't stick: drop it so the next look at this album tries again.
        // "iTunes has nothing" does stick, so a fruitless search isn't repeated all session.
        if (result.Retry) Cache.TryRemove(new KeyValuePair<string, Task<Fetched>>(key, task));

        return result.Image;
    }

    /// <summary>
    /// Warms the cache for an album without waiting for it — used to have the next album's art
    /// ready by the time the user rates the current one. Failures are ignored on purpose.
    /// </summary>
    public static void Prefetch(string artist, string title) => _ = LoadAsync(artist, title);

    private static async Task<Fetched> FetchAsync(string artist, string title)
    {
        try
        {
            byte[]? bytes = ReadFromDisk(CacheFile(artist, title));

            if (bytes is null)
            {
                var album = await AppleMusicCatalog.FindAlbumAsync(artist, title).ConfigureAwait(false);
                string? url = AppleMusicCatalog.ArtworkAtSize(album?.ArtworkUrl, Pixels);
                if (url is null) return new Fetched(null, Retry: false);

                bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                WriteToDisk(CacheFile(artist, title), bytes);
            }

            using var stream = new MemoryStream(bytes);
            var image = new Bitmap(stream);

            // Crude but honest eviction: the working set here is "the album on screen and the one
            // after it", so a full clear costs at most one re-decode from the disk cache.
            if (Cache.Count > MaxCachedImages) Cache.Clear();

            return new Fetched(image, Retry: false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Album art lookup failed for “{title}” — {artist}: {ex.Message}");
            return new Fetched(null, Retry: true);
        }
    }

    // ---------- Disk cache ----------

    /// <summary>Where a given album's cover is cached, named by a hash so any title is a safe file name.</summary>
    private static string CacheFile(string artist, string title)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{artist}|{title}".ToLowerInvariant()));
        return Path.Combine(PlaylistStore.DataDir, "artwork", $"{Convert.ToHexString(hash)[..32]}.jpg");
    }

    private static byte[]? ReadFromDisk(string path)
    {
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static void WriteToDisk(string path, byte[] bytes)
    {
        // A cache that can't be written is only a slower cache, never an error.
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
