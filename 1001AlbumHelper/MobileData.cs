using System.Text;
using System.Text.Json;

namespace _1001AlbumHelper;

/// <summary>
/// Read-only album data for the mobile app. Loaded from snapshots embedded in the shared
/// assembly (see the EmbeddedResource items in the .csproj) so the phone works entirely
/// offline — the desktop app keeps reading its live Google Sheets / data-folder copies.
/// </summary>
public static class MobileData
{
    /// <summary>The 1001 list — number, rating, title, artist, year — in list order.</summary>
    public static List<ViewRow> Load1001()
    {
        using var reader = new StreamReader(Resource("albums-1001.csv"), Encoding.UTF8);
        var rows = new List<ViewRow>();
        bool pastHeader = false;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var cells = ParseCsvLine(line);
            // Skip the rating-legend preamble; the real data starts after the "#, Rating, …" header.
            if (!pastHeader)
            {
                if (cells.Count > 0 && cells[0].Trim() == "#") pastHeader = true;
                continue;
            }
            if (cells.Count < 5) continue;
            string number = cells[0].Trim();
            if (number.Length == 0) continue;
            rows.Add(new ViewRow(number, cells[1].Trim(), cells[2].Trim(), cells[3].Trim(), cells[4].Trim()));
        }
        return rows;
    }

    /// <summary>The potential-replacements shortlist (candidates).</summary>
    public static List<CandidateAlbum> LoadCandidates()
    {
        using var reader = new StreamReader(Resource("candidates.json"), Encoding.UTF8);
        return ReplacementCandidates.Parse(reader.ReadToEnd());
    }

    private static Stream Resource(string logicalName) =>
        typeof(MobileData).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded resource '{logicalName}' not found.");

    /// <summary>Splits one CSV line, honouring double-quoted fields with embedded commas.</summary>
    internal static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } // escaped quote
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
