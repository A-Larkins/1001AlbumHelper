using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace _1001AlbumHelper;

/// <summary>
/// Reads and writes the potentials shortlist to a "Potentials" tab in the Google Sheet, so the Mac
/// and iPhone share one live list.
/// <para>
/// Talks to the Google Sheets REST API directly, authenticating with a service-account key (a signed
/// JWT exchanged for an access token). Deliberately avoids the Google.Apis client library, which is
/// reflection-heavy and breaks under iOS trimming/AOT. Only System.Text.Json + RSA + HttpClient here,
/// all of which are trim- and AOT-safe. The spreadsheet must be shared (Editor) with the service
/// account's email.
/// </para>
/// </summary>
public sealed class CandidateSheet
{
    private const string Api = "https://sheets.googleapis.com/v4/spreadsheets";
    private static readonly string[] Header = { "Title", "Artist", "Genre", "Year", "Status" };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly string _keyJson;
    private readonly string _spreadsheetId;
    private readonly string _tab;
    private (string Token, DateTime Expiry)? _cachedToken;

    private CandidateSheet(string keyJson, string spreadsheetId, string tab)
    {
        _keyJson = keyJson;
        _spreadsheetId = spreadsheetId;
        _tab = tab;
    }

    /// <summary>Builds a client, or null when the key or spreadsheet id is missing.</summary>
    public static CandidateSheet? TryCreate(string? serviceAccountKeyJson, string? spreadsheetId, string tab)
    {
        if (string.IsNullOrWhiteSpace(serviceAccountKeyJson) || string.IsNullOrWhiteSpace(spreadsheetId))
            return null;
        return new CandidateSheet(serviceAccountKeyJson, spreadsheetId!, tab);
    }

    /// <summary>Every candidate on the Potentials tab, in sheet order. An absent tab reads as empty.</summary>
    public async Task<List<CandidateAlbum>> LoadAsync()
    {
        await EnsureTabExistsAsync();

        var (status, body) = await SendAsync(HttpMethod.Get, $"{Api}/{_spreadsheetId}/values/{Range("A2:E")}");
        if (status != 200) throw new Exception($"read {status}: {Trim(body)}");

        var candidates = new List<CandidateAlbum>();
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("values", out var values))
        {
            foreach (var row in values.EnumerateArray())
            {
                string Cell(int i) => i < row.GetArrayLength() ? row[i].GetString()?.Trim() ?? "" : "";
                string title = Cell(0), artist = Cell(1);
                if (title.Length == 0 && artist.Length == 0) continue; // blank row

                candidates.Add(new CandidateAlbum
                {
                    Title = title,
                    Artist = artist,
                    Genre = Cell(2),
                    Year = Cell(3),
                    Status = ParseStatus(Cell(4)),
                });
            }
        }
        return candidates;
    }

    /// <summary>Replaces the Potentials tab with a header row plus one row per candidate.</summary>
    public async Task SaveAsync(IReadOnlyList<CandidateAlbum> candidates)
    {
        await EnsureTabExistsAsync();

        var (cs, cb) = await SendAsync(HttpMethod.Post, $"{Api}/{_spreadsheetId}/values/{Range()}:clear", "{}");
        if (cs != 200) throw new Exception($"clear {cs}: {Trim(cb)}");

        var rows = new List<string[]> { Header };
        rows.AddRange(candidates.Select(c => new[] { c.Title, c.Artist, c.Genre, c.Year, c.Status.ToString() }));

        // RAW so a leading "+"/"=" or a numeric title is stored literally, not parsed as a formula/number.
        var (ws, wb) = await SendAsync(HttpMethod.Put,
            $"{Api}/{_spreadsheetId}/values/{Range("A1")}?valueInputOption=RAW", ValuesBody(rows));
        if (ws != 200) throw new Exception($"write {ws}: {Trim(wb)}");
    }

    private async Task EnsureTabExistsAsync()
    {
        var (status, body) = await SendAsync(HttpMethod.Get, $"{Api}/{_spreadsheetId}?fields=sheets.properties.title");
        if (status != 200) throw new Exception($"meta {status}: {Trim(body)}");

        using (var doc = JsonDocument.Parse(body))
        {
            if (doc.RootElement.TryGetProperty("sheets", out var sheets))
                foreach (var s in sheets.EnumerateArray())
                    if (s.TryGetProperty("properties", out var p) && p.TryGetProperty("title", out var t)
                        && string.Equals(t.GetString(), _tab, StringComparison.Ordinal))
                        return;
        }

        var (a, ab) = await SendAsync(HttpMethod.Post, $"{Api}/{_spreadsheetId}:batchUpdate", AddSheetBody(_tab));
        if (a != 200) throw new Exception($"addSheet {a}: {Trim(ab)}");
    }

    // ---- Service-account auth (signed JWT → access token) ----

    private async Task<string> GetAccessTokenAsync()
    {
        if (_cachedToken is { } c && c.Expiry > DateTime.UtcNow.AddMinutes(1)) return c.Token;

        using var keyDoc = JsonDocument.Parse(_keyJson);
        var root = keyDoc.RootElement;
        string email = root.GetProperty("client_email").GetString()!;
        string privateKeyPem = root.GetProperty("private_key").GetString()!;
        string tokenUri = root.TryGetProperty("token_uri", out var tu) && tu.GetString() is { } u
            ? u : "https://oauth2.googleapis.com/token";

        long iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long exp = iat + 3600;
        string signingInput =
            Base64Url("{\"alg\":\"RS256\",\"typ\":\"JWT\"}") + "." +
            Base64Url($"{{\"iss\":\"{email}\",\"scope\":\"https://www.googleapis.com/auth/spreadsheets\"," +
                      $"\"aud\":\"{tokenUri}\",\"iat\":{iat},\"exp\":{exp}}}");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        byte[] signature = rsa.SignData(Encoding.UTF8.GetBytes(signingInput),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        string jwt = $"{signingInput}.{Base64Url(signature)}";

        using var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
            new KeyValuePair<string, string>("assertion", jwt),
        });
        using var resp = await Http.PostAsync(tokenUri, form);
        string body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new Exception($"auth {(int)resp.StatusCode}: {Trim(body)}");

        using var tokenDoc = JsonDocument.Parse(body);
        string token = tokenDoc.RootElement.GetProperty("access_token").GetString()!;
        _cachedToken = (token, DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime);
        return token;
    }

    private async Task<(int Status, string Body)> SendAsync(HttpMethod method, string url, string? jsonBody = null)
    {
        string token = await GetAccessTokenAsync();
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (jsonBody is not null) req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req);
        return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    // ---- helpers ----

    /// <summary>URL-encoded A1 range for the tab (whole tab, or a cell range like "A2:E").</summary>
    private string Range(string? cells = null)
    {
        string quoted = $"'{_tab.Replace("'", "''")}'";
        return Uri.EscapeDataString(cells is null ? quoted : $"{quoted}!{cells}");
    }

    private static string ValuesBody(IEnumerable<string[]> rows)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteStartArray("values");
            foreach (var row in rows)
            {
                w.WriteStartArray();
                foreach (var cell in row) w.WriteStringValue(cell);
                w.WriteEndArray();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string AddSheetBody(string title)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteStartArray("requests");
            w.WriteStartObject();
            w.WriteStartObject("addSheet");
            w.WriteStartObject("properties");
            w.WriteString("title", title);
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string Base64Url(string s) => Base64Url(Encoding.UTF8.GetBytes(s));
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s;

    private static CandidateStatus ParseStatus(string value) =>
        Enum.TryParse<CandidateStatus>(value, ignoreCase: true, out var status) ? status : CandidateStatus.Pending;
}
