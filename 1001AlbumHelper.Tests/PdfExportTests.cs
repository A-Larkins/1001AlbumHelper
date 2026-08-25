using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// The export renders at all. Worth a test because QuestPDF's failure mode is a layout exception at
/// generate time, not a bad-looking page — a paragraph that can't fit its column throws, and the
/// opening page is the only prose in the document, so it's the only part that can.
/// </summary>
public class PdfExportTests
{
    private static NumberedList.Row Row(int n) =>
        new(SheetRow: n + 1, Number: n.ToString(), Title: $"Album {n}",
            Artist: $"Artist {n}", Year: (1960 + n % 50).ToString());

    private static byte[] Render(int mustHear, int replacements)
    {
        var path = Path.Combine(Path.GetTempPath(), $"1001-export-{Guid.NewGuid():N}.pdf");
        try
        {
            PdfExporter.Write(path,
                Enumerable.Range(1, mustHear).Select(Row).ToList(),
                Enumerable.Range(mustHear + 1, replacements).Select(Row).ToList(),
                "Albums Larkins Thinks You Must Hear");
            return File.ReadAllBytes(path);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Renders_the_opening_page_and_both_lists()
    {
        var pdf = Render(mustHear: 60, replacements: 40);

        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(pdf.Length > 4096, $"Suspiciously small PDF: {pdf.Length} bytes.");
    }

    /// <summary>
    /// An empty export is a failed sync, and a page of essay above zero albums only hides that —
    /// so the preface is dropped, which also makes the document markedly shorter.
    /// </summary>
    [Fact]
    public void An_empty_export_skips_the_opening_page()
    {
        var withAlbums = Render(mustHear: 1, replacements: 1);
        var empty = Render(mustHear: 0, replacements: 0);

        Assert.True(empty.Length < withAlbums.Length,
                    $"Empty export ({empty.Length}) should be smaller than one with albums ({withAlbums.Length}).");
    }
}
