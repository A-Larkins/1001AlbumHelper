using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace _1001AlbumHelper;

/// <summary>
/// Renders the finished list — the ⭐ picks kept from the 1001, plus the albums added to replace
/// the rest — as a single numbered PDF in two sections.
/// </summary>
public static class PdfExporter
{
    /// <summary>One album as it appears in the document.</summary>
    public sealed record Entry(int Number, string Title, string Artist, string Year);

    public sealed record Section(string Heading, string Blurb, IReadOnlyList<Entry> Entries);

    private const string Ink = "#1b1714";
    private const string Muted = "#7a6a5c";
    private const string Rule = "#d9cec3";
    private const string Accent = "#8a6318";

    /// <summary>
    /// Builds the document and writes it to <paramref name="path"/>.
    /// Numbering runs continuously across both sections, so the whole thing reads as one list.
    /// </summary>
    public static void Write(string path, IReadOnlyList<NumberedList.Row> mustHear,
                             IReadOnlyList<NumberedList.Row> replacements, string title)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        int n = 0;
        var fromThe1001 = mustHear
            .Select(r => new Entry(++n, r.Title, r.Artist, r.Year)).ToList();
        var added = replacements
            .Select(r => new Entry(++n, r.Title, r.Artist, r.Year)).ToList();

        var sections = new[]
        {
            new Section(
                "From the 1001",
                "Albums from “1001 Albums You Must Hear Before You Die” that I decided to keep.",
                fromThe1001),
            new Section(
                "My additions",
                "Albums I added in place of the ones I cut, to make the list my own.",
                added),
        };

        int total = fromThe1001.Count + added.Count;

        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Ink));

                page.Header().Element(h => Header(h, title, total));
                page.Content().Element(c => Content(c, sections));
                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8.5f).FontColor(Muted));
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf(path);
    }

    private static void Header(IContainer container, string title, int total)
    {
        container.PaddingBottom(14).Column(col =>
        {
            col.Item().Text(title).FontSize(19).Bold().FontColor(Ink);
            col.Item().PaddingTop(3).Text($"{total} albums · compiled {DateTime.Now:d MMMM yyyy}")
                .FontSize(9).FontColor(Muted);
            col.Item().PaddingTop(9).LineHorizontal(1).LineColor(Rule);
        });
    }

    private static void Content(IContainer container, IReadOnlyList<Section> sections)
    {
        container.Column(col =>
        {
            foreach (var section in sections)
            {
                if (section.Entries.Count == 0) continue;

                col.Item().PaddingTop(14).PaddingBottom(2)
                    .Text(section.Heading).FontSize(13).Bold().FontColor(Accent);
                col.Item().PaddingBottom(8)
                    .Text($"{section.Blurb}  ({section.Entries.Count} albums)")
                    .FontSize(8.5f).FontColor(Muted).Italic();

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(30);   // number
                        c.RelativeColumn(3);    // album
                        c.RelativeColumn(2);    // artist
                        c.ConstantColumn(34);   // year
                    });

                    foreach (var e in section.Entries)
                    {
                        // A hairline under every row keeps long runs readable across a page break.
                        static IContainer Cell(IContainer c) =>
                            c.BorderBottom(0.5f).BorderColor("#efe9e2").PaddingVertical(3);

                        table.Cell().Element(Cell).Text($"{e.Number}")
                            .FontColor(Muted).FontSize(8.5f);
                        table.Cell().Element(Cell).Text(e.Title).SemiBold();
                        table.Cell().Element(Cell).Text(e.Artist).FontColor("#4a423b");
                        table.Cell().Element(Cell).AlignRight().Text(e.Year)
                            .FontColor(Muted).FontSize(8.5f);
                    }
                });
            }
        });
    }
}
