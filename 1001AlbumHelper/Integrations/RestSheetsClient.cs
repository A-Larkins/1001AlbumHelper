using System.Text;
using System.Text.Json;

namespace _1001AlbumHelper;

/// <summary>
/// REST implementation of <see cref="ISheetsClient"/> for a Google service account — the mobile
/// counterpart to <see cref="GoogleSheetsWriter"/>, whose browser-based OAuth login doesn't work on
/// iOS. Talks to the Sheets v4 REST API directly via <see cref="GoogleServiceAccountAuth"/>, the
/// same auth <see cref="CandidateSheet"/> uses — no Google.Apis, so it's trim/AOT-safe on the phone.
/// Every method here is a hand-built JSON translation of the matching
/// <see cref="GoogleSheetsWriter"/> method, so the two behave identically against the same sheet.
/// </summary>
public sealed class RestSheetsClient : ISheetsClient
{
    private const string Api = "https://sheets.googleapis.com/v4/spreadsheets";

    private readonly GoogleServiceAccountAuth _auth;
    private readonly string _spreadsheetId;

    public RestSheetsClient(string serviceAccountKeyJson, string spreadsheetId)
    {
        _auth = new GoogleServiceAccountAuth(serviceAccountKeyJson);
        _spreadsheetId = spreadsheetId;
    }

    public async Task<IReadOnlyList<IReadOnlyList<string>>> ReadTabAsync(string tabName, string a1Range)
    {
        var (status, body) = await _auth.SendAsync(HttpMethod.Get, $"{Api}/{_spreadsheetId}/values/{Range(tabName, a1Range)}");
        if (status != 200) throw new Exception($"read {status}: {GoogleServiceAccountAuth.Trim(body)}");

        var result = new List<IReadOnlyList<string>>();
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("values", out var values))
            foreach (var row in values.EnumerateArray())
                result.Add(row.EnumerateArray().Select(c => c.GetString() ?? "").ToList());
        return result;
    }

    public async Task UpdateCellAsync(string tabName, string cellA1, string value)
    {
        var (status, body) = await _auth.SendAsync(HttpMethod.Put,
            $"{Api}/{_spreadsheetId}/values/{Range(tabName, cellA1)}?valueInputOption=RAW",
            ValuesBody(new[] { new[] { value } }));
        if (status != 200) throw new Exception($"update {status}: {GoogleServiceAccountAuth.Trim(body)}");
    }

    public async Task WriteRangeAsync(string tabName, string topLeft, IList<IList<object>> rows)
    {
        if (rows.Count == 0) return;

        var (status, body) = await _auth.SendAsync(HttpMethod.Put,
            $"{Api}/{_spreadsheetId}/values/{Range(tabName, topLeft)}?valueInputOption=RAW",
            ValuesBody(rows.Select(r => r.Select(c => c?.ToString() ?? "").ToArray())));
        if (status != 200) throw new Exception($"write {status}: {GoogleServiceAccountAuth.Trim(body)}");
    }

    public async Task ClearRangeAsync(string tabName, string a1Range)
    {
        var (status, body) = await _auth.SendAsync(HttpMethod.Post,
            $"{Api}/{_spreadsheetId}/values/{Range(tabName, a1Range)}:clear", "{}");
        if (status != 200) throw new Exception($"clear {status}: {GoogleServiceAccountAuth.Trim(body)}");
    }

    public async Task WriteColumnAsync(string tabName, string column, int startRow, IReadOnlyList<string> values)
    {
        if (values.Count == 0) return;

        int endRow = startRow + values.Count - 1;
        var (status, body) = await _auth.SendAsync(HttpMethod.Put,
            $"{Api}/{_spreadsheetId}/values/{Range(tabName, $"{column}{startRow}:{column}{endRow}")}?valueInputOption=RAW",
            ValuesBody(values.Select(v => new[] { v })));
        if (status != 200) throw new Exception($"write {status}: {GoogleServiceAccountAuth.Trim(body)}");
    }

    /// <summary>
    /// Inserts a blank row via <c>batchUpdate</c> (insertDimension, plus copyPaste to carry over a
    /// neighbour's formatting), then fills it in with a plain values.update — the REST equivalent of
    /// <see cref="GoogleSheetsWriter.InsertRowAsync"/>.
    /// </summary>
    public async Task InsertRowAsync(string tabName, int rowNumber, IList<object> values, int? formatSourceRow = null)
    {
        int sheetId = await GetSheetIdAsync(tabName);

        var (bs, bb) = await _auth.SendAsync(HttpMethod.Post, $"{Api}/{_spreadsheetId}:batchUpdate",
            InsertRowBody(sheetId, rowNumber, formatSourceRow));
        if (bs != 200) throw new Exception($"insertRow {bs}: {GoogleServiceAccountAuth.Trim(bb)}");

        var (ws, wb) = await _auth.SendAsync(HttpMethod.Put,
            $"{Api}/{_spreadsheetId}/values/{Range(tabName, $"A{rowNumber}")}?valueInputOption=RAW",
            ValuesBody(new[] { values.Select(v => v?.ToString() ?? "").ToArray() }));
        if (ws != 200) throw new Exception($"insertRow values {ws}: {GoogleServiceAccountAuth.Trim(wb)}");
    }

    /// <summary>Looks up a tab's numeric id, throwing if it doesn't exist (never creates it).</summary>
    private async Task<int> GetSheetIdAsync(string tabName)
    {
        var (status, body) = await _auth.SendAsync(HttpMethod.Get, $"{Api}/{_spreadsheetId}?fields=sheets.properties");
        if (status != 200) throw new Exception($"meta {status}: {GoogleServiceAccountAuth.Trim(body)}");

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("sheets", out var sheets))
            foreach (var s in sheets.EnumerateArray())
                if (s.TryGetProperty("properties", out var p)
                    && p.TryGetProperty("title", out var t) && string.Equals(t.GetString(), tabName, StringComparison.Ordinal)
                    && p.TryGetProperty("sheetId", out var sid))
                    return sid.GetInt32();

        throw new InvalidOperationException($"Tab \"{tabName}\" doesn't exist in this spreadsheet.");
    }

    /// <summary>Creates the tab if it doesn't already exist. Returns its sheet id either way.</summary>
    public async Task<int> EnsureTabExistsAsync(string tabName)
    {
        try { return await GetSheetIdAsync(tabName); }
        catch (InvalidOperationException)
        {
            var (status, body) = await _auth.SendAsync(HttpMethod.Post, $"{Api}/{_spreadsheetId}:batchUpdate", AddSheetBody(tabName));
            if (status != 200) throw new Exception($"addSheet {status}: {GoogleServiceAccountAuth.Trim(body)}");

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("replies")[0].GetProperty("addSheet")
                .GetProperty("properties").GetProperty("sheetId").GetInt32();
        }
    }

    /// <summary>Deletes a tab outright. Used to clean up a scratch tab created for testing.</summary>
    public async Task DeleteTabAsync(string tabName)
    {
        int sheetId = await GetSheetIdAsync(tabName);
        var (status, body) = await _auth.SendAsync(HttpMethod.Post, $"{Api}/{_spreadsheetId}:batchUpdate", DeleteSheetBody(sheetId));
        if (status != 200) throw new Exception($"deleteSheet {status}: {GoogleServiceAccountAuth.Trim(body)}");
    }

    // ---- request bodies ----

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

    private static string DeleteSheetBody(int sheetId)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteStartArray("requests");
            w.WriteStartObject();
            w.WriteStartObject("deleteSheet");
            w.WriteNumber("sheetId", sheetId);
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string InsertRowBody(int sheetId, int rowNumber, int? formatSourceRow)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteStartArray("requests");

            w.WriteStartObject();
            w.WriteStartObject("insertDimension");
            w.WriteStartObject("range");
            w.WriteNumber("sheetId", sheetId);
            w.WriteString("dimension", "ROWS");
            w.WriteNumber("startIndex", rowNumber - 1); // API is 0-indexed and end-exclusive
            w.WriteNumber("endIndex", rowNumber);
            w.WriteEndObject();
            // Inherit from below when inserting at the very top of the data, so the header's
            // formatting is never picked up — mirrors GoogleSheetsWriter.InsertRowAsync exactly.
            bool inheritFromBefore = formatSourceRow is int src && src <= rowNumber;
            w.WriteBoolean("inheritFromBefore", inheritFromBefore);
            w.WriteEndObject();
            w.WriteEndObject();

            if (formatSourceRow is int source)
            {
                w.WriteStartObject();
                w.WriteStartObject("copyPaste");
                w.WriteStartObject("source");
                w.WriteNumber("sheetId", sheetId);
                w.WriteNumber("startRowIndex", source - 1);
                w.WriteNumber("endRowIndex", source);
                w.WriteEndObject();
                w.WriteStartObject("destination");
                w.WriteNumber("sheetId", sheetId);
                w.WriteNumber("startRowIndex", rowNumber - 1);
                w.WriteNumber("endRowIndex", rowNumber);
                w.WriteEndObject();
                w.WriteString("pasteType", "PASTE_FORMAT");
                w.WriteEndObject();
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string ValuesBody(IEnumerable<IEnumerable<string>> rows)
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

    /// <summary>URL-encoded A1 range for a tab (whole tab, or a cell range like "A2:E").</summary>
    private static string Range(string tabName, string? cells = null)
    {
        string quoted = $"'{tabName.Replace("'", "''")}'";
        return Uri.EscapeDataString(cells is null ? quoted : $"{quoted}!{cells}");
    }
}
