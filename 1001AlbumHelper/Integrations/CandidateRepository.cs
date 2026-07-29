using Microsoft.Extensions.Configuration;

namespace _1001AlbumHelper;

/// <summary>
/// The potentials shortlist, backed by Google Sheets when a service account is configured and always
/// cached to the local JSON file. Callers load the local cache instantly, then <see cref="TryPullAsync"/>
/// refreshes it from Sheets in the background; <see cref="PushAsync"/> writes changes back. When sync
/// isn't set up (no key / no spreadsheet id) both are no-ops, so behaviour is exactly local-only.
/// </summary>
public sealed class CandidateRepository
{
    private readonly CandidateSheet? _sheet; // null = sync not configured

    private CandidateRepository(CandidateSheet? sheet, string status)
    {
        _sheet = sheet;
        Status = status;
    }

    /// <summary>True when a Google Sheets service account is configured and this is running where the key exists.</summary>
    public bool SyncEnabled => _sheet is not null;

    /// <summary>Short human-readable reason for the sync state ("on", "off: …", or "error: …") — for diagnostics.</summary>
    public string Status { get; }

    /// <summary>Reads config + the service-account key and builds a repository (local-only if either is absent).</summary>
    public static CandidateRepository Create()
    {
        try
        {
            var (sheet, status) = BuildSheet();
            return new CandidateRepository(sheet, status);
        }
        catch (Exception ex)
        {
            // Surface the reason instead of hiding it. Unwrap to the root cause — a
            // TypeInitializationException just means a static ctor blew up; the inner one is the real story.
            string type = ex is TypeInitializationException tie ? tie.TypeName ?? ex.GetType().Name : ex.GetType().Name;
            var root = ex;
            while (root.InnerException is not null) root = root.InnerException;
            return new CandidateRepository(null, $"error: {type} → {root.GetType().Name}: {root.Message}");
        }
    }

    private static (CandidateSheet? Sheet, string Status) BuildSheet()
    {
        var builder = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
            .AddJsonFile(Path.Combine(Operations.ProjectDir, "appsettings.json"), optional: true);
        // Mobile has no data folder — fall back to the config baked into the assembly.
        if (Embedded("appsettings.json") is { } configStream) builder.AddJsonStream(configStream);
        var config = builder.Build();

        string? spreadsheetId = config["GoogleSheets:SpreadsheetId"];
        string tab = config["GoogleSheets:PotentialsTab"] ?? "Potentials";
        string keyFile = config["GoogleSheets:ServiceAccountKeyFile"] ?? "service-account.json";

        // Key: the data folder on desktop, else the embedded snapshot on mobile.
        string keyPath = Path.Combine(Operations.ProjectDir, keyFile);
        string? keyJson = File.Exists(keyPath) ? File.ReadAllText(keyPath) : ReadEmbedded("service-account.json");

        if (string.IsNullOrWhiteSpace(keyJson)) return (null, "off: no service-account key");
        if (string.IsNullOrWhiteSpace(spreadsheetId)) return (null, "off: no spreadsheet id");

        var sheet = CandidateSheet.TryCreate(keyJson, spreadsheetId, tab);
        return (sheet, sheet is null ? "off: couldn't build the Sheets client" : "on");
    }

    private static Stream? Embedded(string logicalName) =>
        typeof(CandidateRepository).Assembly.GetManifestResourceStream(logicalName);

    private static string? ReadEmbedded(string logicalName)
    {
        using var stream = Embedded(logicalName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Reads the shortlist from Sheets, or null when sync is off. Does not touch the local cache —
    /// the caller decides whether the sheet wins (adopt it) or is empty and should be seeded from local.
    /// </summary>
    public async Task<List<CandidateAlbum>?> PullAsync()
    {
        if (_sheet is null) return null;
        return await _sheet.LoadAsync();
    }

    /// <summary>Pushes the shortlist up to Sheets (a no-op when sync is off).</summary>
    public async Task PushAsync(IReadOnlyList<CandidateAlbum> candidates)
    {
        if (_sheet is not null) await _sheet.SaveAsync(candidates);
    }
}
