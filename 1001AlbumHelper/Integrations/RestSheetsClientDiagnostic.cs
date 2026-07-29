namespace _1001AlbumHelper;

/// <summary>
/// A headless, self-cleaning check of <see cref="RestSheetsClient"/> — run with `dotnet run --
/// resttest`. This is the client mobile "Rate" support runs on (NumberedList and RatingSession
/// depend only on <see cref="ISheetsClient"/>, which both this and <see cref="GoogleSheetsWriter"/>
/// implement), so it's worth being able to re-verify on demand rather than trusting it by
/// inspection. Exercises every operation, including the trickiest one (<c>InsertRowAsync</c>'s
/// insertDimension + copyPaste formatting), against a scratch tab it creates and deletes — it never
/// touches the real 1001/Must Hear/Replacements tabs. Safe to run repeatedly.
/// </summary>
public static class RestSheetsClientDiagnostic
{
    private const string ScratchTab = "ZZ-RestSheetsClientCheck";

    public static async Task RunAsync()
    {
        var config = EmbeddedConfig.Load("appsettings.json");
        string? spreadsheetId = config["GoogleSheets:SpreadsheetId"];
        string keyFile = config["GoogleSheets:ServiceAccountKeyFile"] ?? "service-account.json";
        string? keyJson = EmbeddedConfig.ReadFileOrEmbedded(keyFile);

        if (string.IsNullOrWhiteSpace(keyJson) || string.IsNullOrWhiteSpace(spreadsheetId))
        {
            Console.WriteLine("✗ Sheets sync isn't configured (missing service-account key or spreadsheet id).");
            return;
        }

        ISheetsClient client = new RestSheetsClient(keyJson, spreadsheetId);
        var scratch = (RestSheetsClient)client;

        try
        {
            Console.WriteLine($"→ Creating scratch tab \"{ScratchTab}\"…");
            await scratch.EnsureTabExistsAsync(ScratchTab);

            Console.WriteLine("→ WriteRangeAsync: seeding a numbered list (header + 3 rows)…");
            await client.WriteRangeAsync(ScratchTab, "A1", new List<IList<object>>
            {
                new List<object> { "#", "Title", "Artist", "Year" },
                new List<object> { "1", "Vs.", "Pearl Jam", "1993" },
                new List<object> { "2", "Aja", "Steely Dan", "1977" },
                new List<object> { "3", "Zuma", "Neil Young", "1975" },
            });

            var read = await client.ReadTabAsync(ScratchTab, "A1:D");
            Check(read.Count == 4, $"WriteRangeAsync/ReadTabAsync round-trip sees {read.Count} rows (expected 4)");
            Check(read[2][1] == "Aja", $"row 2 title reads back as \"{read[2][1]}\" (expected \"Aja\")");

            Console.WriteLine("→ UpdateCellAsync: renaming one title without touching its neighbours…");
            await client.UpdateCellAsync(ScratchTab, "B4", "Zuma (renamed)");
            read = await client.ReadTabAsync(ScratchTab, "A1:D");
            Check(read[3][1] == "Zuma (renamed)", "UpdateCellAsync changed B4");
            Check(read[1][1] == "Vs.", "UpdateCellAsync left an unrelated row (B2) alone");

            Console.WriteLine("→ InsertRowAsync: inserting a new row 3, copying row 2's formatting…");
            await client.InsertRowAsync(ScratchTab, 3,
                new List<object> { "x", "OK Computer", "Radiohead", "1997" }, formatSourceRow: 2);
            read = await client.ReadTabAsync(ScratchTab, "A1:D");
            Check(read.Count == 5, $"5 rows after inserting one (got {read.Count})");
            Check(read[2][1] == "OK Computer", $"the new row landed at row 3: \"{read[2][1]}\"");
            Check(read[3][1] == "Aja", $"the old row 3 (Aja) shifted down to row 4: \"{read[3][1]}\"");
            Check(read[4][1] == "Zuma (renamed)", $"the old row 4 shifted down to row 5: \"{read[4][1]}\"");

            Console.WriteLine("→ WriteColumnAsync: renumbering column A after the insert…");
            await client.WriteColumnAsync(ScratchTab, "A", 2, new[] { "1", "2", "3", "4" });
            read = await client.ReadTabAsync(ScratchTab, "A1:D");
            Check(read[4][0] == "4", $"the last row's number reads \"{read[4][0]}\" (expected \"4\")");

            Console.WriteLine("→ ClearRangeAsync: blanking the last row…");
            await client.ClearRangeAsync(ScratchTab, "A5:D5");
            read = await client.ReadTabAsync(ScratchTab, "A1:D");
            Check(read.Count == 4, $"back to 4 rows once the trailing one is cleared (got {read.Count})");

            Console.WriteLine("✓ Every ISheetsClient operation round-tripped correctly.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ REST CLIENT ERROR: {ex}");
        }
        finally
        {
            Console.WriteLine($"→ Deleting scratch tab \"{ScratchTab}\"…");
            try
            {
                await scratch.DeleteTabAsync(ScratchTab);
                Console.WriteLine("✓ Cleaned up — nothing left behind in the real spreadsheet.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Couldn't delete the scratch tab automatically — remove \"{ScratchTab}\" by hand: {ex.Message}");
            }
        }
    }

    private static void Check(bool condition, string description) =>
        Console.WriteLine(condition ? $"  ✓ {description}" : $"  ✗ FAILED: {description}");
}
