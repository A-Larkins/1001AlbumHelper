namespace _1001AlbumHelper;

/// <summary>
/// The phone's way in to the master spreadsheet — the 1001, Must Hear and Replacements tabs.
/// <para>
/// Desktop builds its client from a credentials file plus a cached OAuth token; the phone has
/// neither, so it reads the same config and service-account key through <see cref="EmbeddedConfig"/>
/// and talks to Sheets over REST (<see cref="RestSheetsClient"/> — Google.Apis can't run on iOS, see
/// <see cref="CandidateSheet"/> for why). Same spreadsheet either way; only the transport differs.
/// </para>
/// <para>
/// Shaped like <see cref="CandidateRepository"/>: always returns an instance, with
/// <see cref="SyncEnabled"/> false and a readable <see cref="Status"/> when sync isn't set up here,
/// so callers can say why instead of failing silently. Note this covers the *master* spreadsheet;
/// <see cref="CandidateRepository"/> is the separate Potentials-tab shortlist.
/// </para>
/// </summary>
public sealed class MobileSheets
{
    private MobileSheets(ISheetsClient? client, string status,
                         string albumsTab, string starredTab, string replacementsTab)
    {
        Client = client;
        Status = status;
        AlbumsTab = albumsTab;
        StarredTab = starredTab;
        ReplacementsTab = replacementsTab;
    }

    /// <summary>Null when sync isn't configured on this device.</summary>
    public ISheetsClient? Client { get; }

    public bool SyncEnabled => Client is not null;

    /// <summary>Short human-readable reason for the sync state ("on", "off: …", or "error: …").</summary>
    public string Status { get; }

    public string AlbumsTab { get; }
    public string StarredTab { get; }
    public string ReplacementsTab { get; }

    public static MobileSheets Create()
    {
        var config = EmbeddedConfig.Load("appsettings.json");
        string albumsTab = config["GoogleSheets:AlbumsTab"] ?? "1001 albums";
        string starredTab = config["GoogleSheets:StarredTab"] ?? "Must Hear";
        string replacementsTab = config["GoogleSheets:ReplacementsTab"] ?? "Replacements";

        MobileSheets Off(string status) =>
            new(null, status, albumsTab, starredTab, replacementsTab);

        try
        {
            string? spreadsheetId = config["GoogleSheets:SpreadsheetId"];
            string keyFile = config["GoogleSheets:ServiceAccountKeyFile"] ?? "service-account.json";
            string? keyJson = EmbeddedConfig.ReadFileOrEmbedded(keyFile);

            if (string.IsNullOrWhiteSpace(keyJson)) return Off("off: no service-account key");
            if (string.IsNullOrWhiteSpace(spreadsheetId)) return Off("off: no spreadsheet id");

            return new MobileSheets(new RestSheetsClient(keyJson, spreadsheetId), "on",
                                    albumsTab, starredTab, replacementsTab);
        }
        catch (Exception ex)
        {
            // Unwrap to the root cause — a TypeInitializationException only says a static ctor blew
            // up; the inner exception is the real story. Same reasoning as CandidateRepository.
            var root = ex;
            while (root.InnerException is not null) root = root.InnerException;
            return Off($"error: {root.GetType().Name}: {root.Message}");
        }
    }
}
