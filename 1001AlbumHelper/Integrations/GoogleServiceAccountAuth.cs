using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace _1001AlbumHelper;

/// <summary>
/// Exchanges a Google service-account key for a Sheets access token (a JWT, signed with the key's
/// private RSA key, traded for a bearer token) — the only auth style that works on iOS with no
/// login screen, unlike the desktop list-writer's browser-based OAuth. Only System.Text.Json + RSA +
/// HttpClient, all trim/AOT-safe, so this — and everything built on it — can run on the phone.
/// <para>
/// Shared by every REST-based Sheets client (<see cref="CandidateSheet"/>, <see cref="RestSheetsClient"/>)
/// so the JWT plumbing exists exactly once.
/// </para>
/// </summary>
public sealed class GoogleServiceAccountAuth
{
    private const string Scope = "https://www.googleapis.com/auth/spreadsheets";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly string _keyJson;
    private (string Token, DateTime Expiry)? _cachedToken;

    public GoogleServiceAccountAuth(string keyJson) => _keyJson = keyJson;

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
            Base64Url($"{{\"iss\":\"{email}\",\"scope\":\"{Scope}\"," +
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

    /// <summary>Sends an authenticated request, returning the status code and raw body.</summary>
    public async Task<(int Status, string Body)> SendAsync(HttpMethod method, string url, string? jsonBody = null)
    {
        string token = await GetAccessTokenAsync();
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (jsonBody is not null) req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req);
        return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    private static string Base64Url(string s) => Base64Url(Encoding.UTF8.GetBytes(s));
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Shortens a response body for an exception message.</summary>
    public static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
