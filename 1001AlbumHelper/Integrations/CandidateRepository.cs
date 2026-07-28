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

    private CandidateRepository(CandidateSheet? sheet) => _sheet = sheet;

    /// <summary>True when a Google Sheets service account is configured and this is running where the key exists.</summary>
    public bool SyncEnabled => _sheet is not null;

    /// <summary>Reads config + the service-account key and builds a repository (local-only if either is absent).</summary>
    public static CandidateRepository Create()
    {
        CandidateSheet? sheet = null;
        try { sheet = BuildSheet(); }
        catch { /* any config/key problem → local-only, never throw at startup */ }
        return new CandidateRepository(sheet);
    }

    private static CandidateSheet? BuildSheet()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
            .AddJsonFile(Path.Combine(Operations.ProjectDir, "appsettings.json"), optional: true)
            .Build();

        string? spreadsheetId = config["GoogleSheets:SpreadsheetId"];
        string tab = config["GoogleSheets:PotentialsTab"] ?? "Potentials";
        string keyFile = config["GoogleSheets:ServiceAccountKeyFile"] ?? "service-account.json";

        // Desktop: the key sits in the data folder next to appsettings.json. (Mobile will supply the
        // key another way; until then BuildSheet returns null there and it stays local-only.)
        string keyPath = Path.Combine(Operations.ProjectDir, keyFile);
        if (!File.Exists(keyPath)) return null;

        return CandidateSheet.TryCreate(File.ReadAllText(keyPath), spreadsheetId, tab);
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
