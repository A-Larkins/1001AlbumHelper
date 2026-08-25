using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace _1001AlbumHelper;

/// <summary>
/// Renders the finished list — the ⭐ picks kept from the 1001, plus the albums added to replace
/// the rest — as a single numbered PDF: an opening page about where the list comes from and what
/// the rules for it are, then the albums themselves in two numbered sections.
/// </summary>
public static class PdfExporter
{
    /// <summary>One album as it appears in the document.</summary>
    public sealed record Entry(int Number, string Title, string Artist, string Year);

    public sealed record Section(string Heading, string Blurb, IReadOnlyList<Entry> Entries);

    /// <summary>A headed block of prose on the opening page.</summary>
    private sealed record Note(string Heading, IReadOnlyList<string> Paragraphs);

    /// <summary>
    /// The opening page. Two things belong here and nowhere else: what the book this list answers
    /// to actually is, and where this list knowingly parts company with it — chiefly that a great
    /// EP counts, which the original's own rules would never have allowed.
    /// </summary>
    private static readonly Note[] Preface =
    {
        new("Where this comes from", new[]
        {
            "“1001 Albums You Must Hear Before You Die” was first published in 2005 by Universe "
            + "Publishing under general editor Robert Dimery, who put the picks to a panel of music "
            + "critics: the most important, influential and best in popular music from the 1950s "
            + "onwards. The original ran to 960 pages, every album carrying a short essay by a "
            + "critic alongside its running time, its producer and a picture of the sleeve.",

            "It has been revised over and over, and no two editions hold the same 1001 — albums are "
            + "dropped to make room. The 2005 edition opens on Frank Sinatra’s “In the Wee Small "
            + "Hours” and closes on The White Stripes’ “Get Behind Me Satan”; later ones end on "
            + "Yeah Yeah Yeahs’ “It’s Blitz!” (2010), Bowie’s “The Next Day” (2013) and “Blackstar” "
            + "(2016), Hookworms’ “Microshift” (2018) and Jazmine Sullivan’s “Heaux Tales” (2021).",

            "A record is dated by its first release in the artist’s home country rather than by "
            + "whatever reissue you happen to own — the same rule this list follows. By the count of "
            + "the 2017 edition the most represented names are David Bowie, Neil Young and the "
            + "Beatles (taking the members’ own records with the band’s) at nine albums each, then "
            + "George Harrison at eight, then Bob Dylan and Brian Eno at seven.",

            "Not everyone was convinced. Publishers Weekly called it “about as comprehensive a "
            + "‘best-of’ as any sane person could want”; Matt Price, writing in The Australian in "
            + "November 2005, called the whole premise of the book humbug — “biased, un-Australian "
            + "and unacceptable”. Which is roughly the argument this list is my side of.",
        }),

        new("The rules — theirs, and mine", new[]
        {
            "The book’s panel ruled several things out before it started: various-artist "
            + "compilations, most film soundtracks and anything else built on non-original "
            + "material, classical music, and modern art music. No EPs either — the book is about "
            + "albums, and a short record was never in the running.",

            "I keep most of that. The one rule I have deliberately let go is the last one: "
            + "an EP counts here if it is awesome. A great record that happens to be short is "
            + "still a great record, and leaving it out to protect a definition seemed a worse "
            + "answer than letting it in. Everything else on the panel’s no list stays off mine.",
        }),
    };

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

        // Nothing to preface when there's nothing to preface — an empty export is a failed sync,
        // and a page of essay above zero albums only obscures that.
        var preface = total > 0 ? Preface : Array.Empty<Note>();

        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Ink));

                page.Header().Element(h => Header(h, title, total));
                page.Content().Element(c => Content(c, sections, preface));
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

    private static void Content(IContainer container, IReadOnlyList<Section> sections,
                                IReadOnlyList<Note> preface)
    {
        container.Column(col =>
        {
            foreach (var note in preface)
            {
                col.Item().PaddingTop(14).PaddingBottom(6)
                    .Text(note.Heading).FontSize(13).Bold().FontColor(Accent);

                foreach (var paragraph in note.Paragraphs)
                {
                    col.Item().PaddingBottom(7).Text(paragraph)
                        .FontSize(9).FontColor("#4a423b").LineHeight(1.45f);
                }
            }

            // The albums start on a page of their own, so the list isn't half-buried under prose.
            if (preface.Count > 0) col.Item().PageBreak();

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
