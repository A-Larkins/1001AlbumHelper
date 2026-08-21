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
        var config = EmbeddedConfig.Load("appsettings.json");

        string? spreadsheetId = config["GoogleSheets:SpreadsheetId"];
        string tab = config["GoogleSheets:PotentialsTab"] ?? "Potentials";
        string keyFile = config["GoogleSheets:ServiceAccountKeyFile"] ?? "service-account.json";
        string? keyJson = EmbeddedConfig.ReadFileOrEmbedded(keyFile);

        if (string.IsNullOrWhiteSpace(keyJson)) return (null, "off: no service-account key");
        if (string.IsNullOrWhiteSpace(spreadsheetId)) return (null, "off: no spreadsheet id");

        var sheet = CandidateSheet.TryCreate(keyJson, spreadsheetId, tab);
        return (sheet, sheet is null ? "off: couldn't build the Sheets client" : "on");
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

    /// <summary>
    /// Pushes the shortlist up to Sheets (a no-op when sync is off).
    /// <para>
    /// The write is a merge, not a replacement: the sheet is read first and <paramref name="candidates"/>
    /// folded into it (see <see cref="ReplacementCandidates.Merge"/>). Callers hold the shortlist for as
    /// long as their screen is open, so by the time one saves, the sheet may have grown underneath it —
    /// a Playlist 2 pull adds candidates from the other device. Writing the held copy wholesale would
    /// delete those, which is a wipe rather than a save; merging can only ever add. The extra read costs
    /// one round trip on a path that runs once per decision.
    /// </para>
    /// </summary>
    public async Task PushAsync(IReadOnlyList<CandidateAlbum> candidates)
    {
        if (_sheet is null) return;

        var onSheet = await _sheet.LoadAsync();
        await _sheet.SaveAsync(ReplacementCandidates.Merge(onSheet, candidates));
    }
}
