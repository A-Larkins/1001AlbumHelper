using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Util.Store;

namespace _1001AlbumHelper;

/// <summary>
/// Writes generated album lists into a Google Sheet. Authenticates with OAuth as the
/// signed-in user (a browser opens the first time; the token is then cached and reused).
/// </summary>
public sealed class GoogleSheetsWriter
{
    private readonly SheetsService _service;
    private readonly string _spreadsheetId;

    private GoogleSheetsWriter(SheetsService service, string spreadsheetId)
    {
        _service = service;
        _spreadsheetId = spreadsheetId;
    }

    public static async Task<GoogleSheetsWriter> CreateAsync(
        string spreadsheetId, string credentialsPath, string tokenStorePath)
    {
        UserCredential credential;
        await using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
        {
            var secrets = (await GoogleClientSecrets.FromStreamAsync(stream)).Secrets;
            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                new[] { SheetsService.Scope.Spreadsheets },
                "user",
                CancellationToken.None,
                new FileDataStore(tokenStorePath, fullPath: true));
        }

        var service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "1001 Albums Helper",
        });

        return new GoogleSheetsWriter(service, spreadsheetId);
    }

    /// <summary>Returns the titles of every tab in the spreadsheet (read-only).</summary>
    public async Task<IReadOnlyList<string>> GetTabTitlesAsync()
    {
        var spreadsheet = await _service.Spreadsheets.Get(_spreadsheetId).ExecuteAsync();
        return spreadsheet.Sheets.Select(s => s.Properties.Title).ToList();
    }

    /// <summary>Replaces the entire contents of <paramref name="tabName"/> with <paramref name="rows"/>.</summary>
    public async Task WriteTabAsync(string tabName, IList<IList<object>> rows)
    {
        int sheetId = await EnsureTabExistsAsync(tabName);
        string range = QuoteTab(tabName); // A1 notation must quote names with spaces / '*' etc.

        // Clear every value currently in the tab.
        await _service.Spreadsheets.Values
            .Clear(new ClearValuesRequest(), _spreadsheetId, range)
            .ExecuteAsync();

        // Reset the font size across the whole tab so stale/oversized formatting
        // (e.g. a leftover big-font row) can't linger under the new values.
        var normalize = new BatchUpdateSpreadsheetRequest
        {
            Requests = new List<Request>
            {
                new Request
                {
                    RepeatCell = new RepeatCellRequest
                    {
                        Range = new GridRange { SheetId = sheetId },
                        Cell = new CellData
                        {
                            UserEnteredFormat = new CellFormat { TextFormat = new TextFormat { FontSize = 10 } }
                        },
                        Fields = "userEnteredFormat.textFormat.fontSize"
                    }
                }
            }
        };
        await _service.Spreadsheets.BatchUpdate(normalize, _spreadsheetId).ExecuteAsync();

        // Write the new values from A1.
        var update = _service.Spreadsheets.Values.Update(
            new ValueRange { Values = rows }, _spreadsheetId, $"{range}!A1");
        update.ValueInputOption =
            SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
        await update.ExecuteAsync();
    }

    // Wrap a tab name in single quotes for A1 notation, escaping any embedded quotes.
    private static string QuoteTab(string tabName) => "'" + tabName.Replace("'", "''") + "'";

    /// <summary>Ensures the tab exists and returns its numeric sheet id.</summary>
    private async Task<int> EnsureTabExistsAsync(string tabName)
    {
        var spreadsheet = await _service.Spreadsheets.Get(_spreadsheetId).ExecuteAsync();
        var existing = spreadsheet.Sheets.FirstOrDefault(
            s => string.Equals(s.Properties.Title, tabName, StringComparison.Ordinal));
        if (existing != null) return existing.Properties.SheetId ?? 0;

        var request = new BatchUpdateSpreadsheetRequest
        {
            Requests = new List<Request>
            {
                new Request
                {
                    AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = tabName } }
                }
            }
        };
        var response = await _service.Spreadsheets.BatchUpdate(request, _spreadsheetId).ExecuteAsync();
        return response.Replies[0].AddSheet.Properties.SheetId ?? 0;
    }
}
