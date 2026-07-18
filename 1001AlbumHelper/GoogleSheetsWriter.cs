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

    /// <summary>Replaces the entire contents of <paramref name="tabName"/> with <paramref name="rows"/>.</summary>
    public async Task WriteTabAsync(string tabName, IList<IList<object>> rows)
    {
        await EnsureTabExistsAsync(tabName);

        // Clear whatever is currently in the tab, then write from A1.
        await _service.Spreadsheets.Values
            .Clear(new ClearValuesRequest(), _spreadsheetId, tabName)
            .ExecuteAsync();

        var update = _service.Spreadsheets.Values.Update(
            new ValueRange { Values = rows }, _spreadsheetId, $"{tabName}!A1");
        update.ValueInputOption =
            SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
        await update.ExecuteAsync();
    }

    private async Task EnsureTabExistsAsync(string tabName)
    {
        var spreadsheet = await _service.Spreadsheets.Get(_spreadsheetId).ExecuteAsync();
        bool exists = spreadsheet.Sheets.Any(
            s => string.Equals(s.Properties.Title, tabName, StringComparison.Ordinal));
        if (exists) return;

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
        await _service.Spreadsheets.BatchUpdate(request, _spreadsheetId).ExecuteAsync();
    }
}
