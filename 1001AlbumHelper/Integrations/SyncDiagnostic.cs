namespace _1001AlbumHelper;

/// <summary>
/// A headless check of the Google Sheets potentials sync, run with `dotnet run -- synctest`.
/// Confirms the service account can reach the sheet, and seeds the Potentials tab from the local
/// shortlist on first setup. Prints what it finds; safe to run repeatedly.
/// </summary>
public static class SyncDiagnostic
{
    public static async Task RunAsync()
    {
        var repo = CandidateRepository.Create();
        Console.WriteLine($"Sync configured: {repo.SyncEnabled}");
        if (!repo.SyncEnabled)
        {
            Console.WriteLine("→ Local only. Missing service-account.json or SpreadsheetId.");
            return;
        }

        try
        {
            var remote = await repo.PullAsync();
            var local = ReplacementCandidates.Load();
            Console.WriteLine($"Sheet 'Potentials' rows: {remote?.Count ?? 0}");
            Console.WriteLine($"Local shortlist rows:    {local.Count}");

            if ((remote?.Count ?? 0) == 0 && local.Count > 0)
            {
                Console.WriteLine($"→ Seeding the sheet with {local.Count} local candidates…");
                await repo.PushAsync(local);
                var after = await repo.PullAsync();
                Console.WriteLine($"→ After seed, sheet rows: {after?.Count ?? 0}");
            }

            Console.WriteLine("✓ Sync works — the service account can read and write the sheet.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ SYNC ERROR: {ex.Message}");
            Console.WriteLine("  (Most likely the sheet isn't shared with the service account's email yet.)");
        }
    }
}
