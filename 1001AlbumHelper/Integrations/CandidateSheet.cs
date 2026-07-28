using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace _1001AlbumHelper;

/// <summary>
/// Reads and writes the potentials shortlist to a "Potentials" tab in the Google Sheet, so the Mac
/// and iPhone share one live list.
/// <para>
/// Authenticates with a Google <b>service account</b> (from a key file), which — unlike the browser
/// OAuth the desktop list-writer uses — works identically on both platforms with no login screen.
/// The spreadsheet must be shared (Editor) with the service account's email.
/// </para>
/// </summary>
public sealed class CandidateSheet
{
    // The Potentials tab layout: one header row, then a row per candidate.
    private static readonly string[] Header = { "Title", "Artist", "Genre", "Year", "Status" };

    private readonly SheetsService _service;
    private readonly string _spreadsheetId;
    private readonly string _tab;

    private CandidateSheet(SheetsService service, string spreadsheetId, string tab)
    {
        _service = service;
        _spreadsheetId = spreadsheetId;
        _tab = tab;
    }

    /// <summary>
    /// Builds a client from a service-account key JSON, or null when the key or spreadsheet id is
    /// missing (so callers can quietly fall back to the local cache when sync isn't configured).
    /// </summary>
    public static CandidateSheet? TryCreate(string? serviceAccountKeyJson, string? spreadsheetId, string tab)
    {
        if (string.IsNullOrWhiteSpace(serviceAccountKeyJson) || string.IsNullOrWhiteSpace(spreadsheetId))
            return null;

        var credential = GoogleCredential.FromJson(serviceAccountKeyJson)
            .CreateScoped(SheetsService.Scope.Spreadsheets);
        var service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "1001 Albums Helper",
        });
        return new CandidateSheet(service, spreadsheetId!, tab);
    }

    /// <summary>Every candidate on the Potentials tab, in sheet order.</summary>
    public async Task<List<CandidateAlbum>> LoadAsync()
    {
        var response = await _service.Spreadsheets.Values
            .Get(_spreadsheetId, $"{Quote(_tab)}!A2:E")
            .ExecuteAsync();

        var candidates = new List<CandidateAlbum>();
        foreach (var row in response.Values ?? new List<IList<object>>())
        {
            string Cell(int i) => i < row.Count ? row[i]?.ToString()?.Trim() ?? "" : "";

            string title = Cell(0), artist = Cell(1);
            if (title.Length == 0 && artist.Length == 0) continue; // skip blank rows

            candidates.Add(new CandidateAlbum
            {
                Title = title,
                Artist = artist,
                Genre = Cell(2),
                Year = Cell(3),
                Status = ParseStatus(Cell(4)),
            });
        }
        return candidates;
    }

    /// <summary>
    /// Replaces the Potentials tab with <paramref name="candidates"/> — a header row plus one row
    /// each. Clears first so a shrunk list can't leave stragglers behind.
    /// </summary>
    public async Task SaveAsync(IReadOnlyList<CandidateAlbum> candidates)
    {
        await EnsureTabExistsAsync();

        var values = new List<IList<object>> { Header.Cast<object>().ToList() };
        values.AddRange(candidates.Select(c => (IList<object>)new List<object>
        {
            c.Title, c.Artist, c.Genre, c.Year, c.Status.ToString(),
        }));

        await _service.Spreadsheets.Values
            .Clear(new ClearValuesRequest(), _spreadsheetId, Quote(_tab))
            .ExecuteAsync();

        var update = _service.Spreadsheets.Values.Update(
            new ValueRange { Values = values }, _spreadsheetId, $"{Quote(_tab)}!A1");
        // RAW so a leading "+"/"=" or an accented title is stored literally, not parsed as a formula.
        update.ValueInputOption =
            SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await update.ExecuteAsync();
    }

    /// <summary>Creates the Potentials tab if the spreadsheet doesn't have it yet.</summary>
    private async Task EnsureTabExistsAsync()
    {
        var spreadsheet = await _service.Spreadsheets.Get(_spreadsheetId).ExecuteAsync();
        if (spreadsheet.Sheets.Any(s => string.Equals(s.Properties.Title, _tab, StringComparison.Ordinal)))
            return;

        await _service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest
        {
            Requests = new List<Request>
            {
                new Request { AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = _tab } } }
            }
        }, _spreadsheetId).ExecuteAsync();
    }

    private static CandidateStatus ParseStatus(string value) =>
        Enum.TryParse<CandidateStatus>(value, ignoreCase: true, out var status) ? status : CandidateStatus.Pending;

    // A1 notation must quote tab names that contain spaces / punctuation; '' escapes a literal quote.
    private static string Quote(string tab) => $"'{tab.Replace("'", "''")}'";
}
