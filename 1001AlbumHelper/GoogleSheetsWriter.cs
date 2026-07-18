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

        // Reset ALL cell formatting on the tab to a clean default (10pt font, no borders,
        // default alignment/background). "userEnteredFormat" in the field mask replaces the
        // whole format, so leftover formatting (a big-font row, stray borders, etc.) can't
        // make any row look different. A second request keeps the header row bold.
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
                        Fields = "userEnteredFormat"
                    }
                },
                new Request
                {
                    RepeatCell = new RepeatCellRequest
                    {
                        Range = new GridRange { SheetId = sheetId, StartRowIndex = 0, EndRowIndex = 1 },
                        Cell = new CellData
                        {
                            UserEnteredFormat = new CellFormat { TextFormat = new TextFormat { Bold = true } }
                        },
                        Fields = "userEnteredFormat.textFormat.bold"
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

    /// <summary>
    /// Reads a tab's cells as raw strings. Short rows are returned as-is, so callers must
    /// index defensively. Read-only — nothing is created or modified.
    /// </summary>
    public async Task<IReadOnlyList<IReadOnlyList<string>>> ReadTabAsync(string tabName, string a1Range)
    {
        var request = _service.Spreadsheets.Values.Get(_spreadsheetId, $"{QuoteTab(tabName)}!{a1Range}");
        // Keep trailing empty cells so column positions stay meaningful.
        request.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.FORMATTEDVALUE;
        var response = await request.ExecuteAsync();

        var result = new List<IReadOnlyList<string>>();
        foreach (var row in response.Values ?? new List<IList<object>>())
            result.Add(row.Select(c => c?.ToString() ?? "").ToList());
        return result;
    }

    /// <summary>
    /// Writes a single cell, leaving every other cell — and all formatting — untouched.
    /// This is the only safe way to touch the master album list, which
    /// <see cref="WriteTabAsync"/> would otherwise clear and reformat wholesale.
    /// </summary>
    public async Task UpdateCellAsync(string tabName, string cellA1, string value)
    {
        var update = _service.Spreadsheets.Values.Update(
            new ValueRange { Values = new List<IList<object>> { new List<object> { value } } },
            _spreadsheetId,
            $"{QuoteTab(tabName)}!{cellA1}");
        // RAW so an emoji or a leading "+"/"=" is stored literally rather than parsed as a formula.
        update.ValueInputOption =
            SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await update.ExecuteAsync();
    }

    /// <summary>Looks up a tab's numeric id, throwing if it doesn't exist (never creates it).</summary>
    public async Task<int> GetSheetIdAsync(string tabName)
    {
        var spreadsheet = await _service.Spreadsheets.Get(_spreadsheetId).ExecuteAsync();
        var sheet = spreadsheet.Sheets.FirstOrDefault(
            s => string.Equals(s.Properties.Title, tabName, StringComparison.Ordinal));
        if (sheet is null)
            throw new InvalidOperationException($"Tab \"{tabName}\" doesn't exist in this spreadsheet.");
        return sheet.Properties.SheetId ?? 0;
    }

    /// <summary>
    /// Inserts a blank row above <paramref name="rowNumber"/> (1-indexed) and fills it in.
    /// The blank row inherits formatting from the row above, and everything below shifts down —
    /// so unlike <see cref="WriteTabAsync"/> this preserves the rest of the tab.
    /// </summary>
    public async Task InsertRowAsync(string tabName, int rowNumber, IList<object> values)
    {
        int sheetId = await GetSheetIdAsync(tabName);

        var insert = new BatchUpdateSpreadsheetRequest
        {
            Requests = new List<Request>
            {
                new Request
                {
                    InsertDimension = new InsertDimensionRequest
                    {
                        Range = new DimensionRange
                        {
                            SheetId = sheetId,
                            Dimension = "ROWS",
                            StartIndex = rowNumber - 1, // API is 0-indexed and end-exclusive
                            EndIndex = rowNumber,
                        },
                        InheritFromBefore = true,
                    }
                }
            }
        };
        await _service.Spreadsheets.BatchUpdate(insert, _spreadsheetId).ExecuteAsync();

        var update = _service.Spreadsheets.Values.Update(
            new ValueRange { Values = new List<IList<object>> { values } },
            _spreadsheetId,
            $"{QuoteTab(tabName)}!A{rowNumber}");
        update.ValueInputOption =
            SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await update.ExecuteAsync();
    }

    /// <summary>Overwrites a single column from <paramref name="startRow"/> down. Other columns are untouched.</summary>
    public async Task WriteColumnAsync(string tabName, string column, int startRow, IReadOnlyList<string> values)
    {
        if (values.Count == 0) return;

        var cells = values.Select(v => (IList<object>)new List<object> { v }).ToList();
        int endRow = startRow + values.Count - 1;

        var update = _service.Spreadsheets.Values.Update(
            new ValueRange { Values = cells },
            _spreadsheetId,
            $"{QuoteTab(tabName)}!{column}{startRow}:{column}{endRow}");
        update.ValueInputOption =
            SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
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
