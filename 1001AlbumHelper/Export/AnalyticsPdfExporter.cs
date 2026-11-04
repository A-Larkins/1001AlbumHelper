using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace _1001AlbumHelper;

/// <summary>
/// The Insights page on paper, in the same visual language as <see cref="PdfExporter"/>: one page
/// for the 1001 (what you've heard and how you rated it), one for the albums you added and the
/// shortlist. Drawn from the same <see cref="AlbumAnalytics.Report"/> the page shows, so the two
/// can't disagree. <see cref="AddPages"/> is also how the finished-list PDF carries them at its end.
/// </summary>
public static class AnalyticsPdfExporter
{
    private const string Ink = "#1b1714";
    private const string Muted = "#7a6a5c";
    private const string Rule = "#d9cec3";
    private const string Accent = "#8a6318";
    private const string BarTrack = "#efe9e2";

    /// <summary>
    /// ⭐ → ❌, one hue stepping lighter on paper (the reverse of the dark screen's ramp), then ✓ —
    /// heard, never scored — in a neutral gray, since it isn't a step on that scale.
    /// </summary>
    private static readonly string[] MarkInk = { "#a8741f", "#cf9d4f", "#e3c48f", "#f1e2c8", "#b9b3ad" };

    private static readonly string[] MarkLabels =
        RatingSession.Choices.Select(c => c.Label).Append("Heard, not scored").ToArray();

    /// <summary>Your additions beside the 1001: the accent against a neutral.</summary>
    private const string YoursInk = "#a8741f";
    private const string The1001Ink = "#c9c2bb";

    /// <summary>The Insights pages as a document of their own.</summary>
    public static void Write(string path, AlbumAnalytics.Report report)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        Document.Create(doc => AddPages(doc, report)).GeneratePdf(path);
    }

    /// <summary>Adds the Insights pages to a document being built — on their own, or after the list.</summary>
    public static void AddPages(IDocumentContainer doc, AlbumAnalytics.Report report)
    {
        Page(doc, "Taste Patterns — The 1001",
            $"{report.Taste.HeardAlbums} of {report.Taste.TotalAlbums} albums heard",
            col => The1001(col, report));
        Page(doc, "Taste Patterns — Your Additions",
            $"{report.Additions.Count} albums added · {report.ShortlistPending} on the shortlist",
            col => Additions(col, report));
    }

    private static void Page(IDocumentContainer doc, string title, string subtitle, Action<ColumnDescriptor> body)
    {
        doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.5f, Unit.Centimetre);
            page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Ink));

            page.Header().PaddingBottom(14).Column(col =>
            {
                col.Item().Text(title).FontSize(19).Bold().FontColor(Ink);
                col.Item().PaddingTop(3).Text($"{subtitle} · compiled {DateTime.Now:MMMM d, yyyy}")
                    .FontSize(9).FontColor(Muted);
                col.Item().PaddingTop(9).LineHorizontal(1).LineColor(Rule);
            });
            page.Content().Column(body);
            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8.5f).FontColor(Muted));
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });
    }

    // ---------- The 1001 ----------

    private static void The1001(ColumnDescriptor col, AlbumAnalytics.Report report)
    {
        var taste = report.Taste;
        var mix = taste.Mix;

        col.Item().Element(c => Tiles(c,
            ($"{taste.HeardAlbums} / {taste.TotalAlbums}", "HEARD",
                $"{Pct(taste.HeardAlbums, taste.TotalAlbums)} of the list"),
            (mix.Heard > 0 ? mix.StarShare.ToString("P0") : "—", "⭐ RATE",
                $"{mix.Starred} of {mix.Heard} heard got a ⭐"),
            (taste.FavoriteGenre?.Label ?? "—", "FAVORITE GENRE", Detail(taste.FavoriteGenre)),
            (taste.FavoriteDecade?.Label ?? "—", "BEST DECADE", Detail(taste.FavoriteDecade))));

        col.Item().PaddingTop(14).Element(c => MixBar(c, mix));
        col.Item().PaddingTop(4)
            .Text("A ✓ is an album you listened to and never scored. None of them got a ⭐, so they count as heard but not starred.")
            .FontSize(7.5f).FontColor(Muted);

        col.Item().PaddingTop(16).Row(row =>
        {
            row.RelativeItem().Element(c => BarTable(c, "By decade", report.Decades, fill: false));
            row.ConstantItem(24);
            row.RelativeItem().Element(c => BarTable(c, "Most heard genres", report.MostHeardGenres, fill: false));
        });

        col.Item().PaddingTop(14).Row(row =>
        {
            row.RelativeItem().Element(c => BarTable(c, "Favorite genres", report.FavoriteGenres, fill: true));
            row.ConstantItem(24);
            row.RelativeItem().Element(c => BarTable(c, "Least favorite genres", report.LeastFavoriteGenres, fill: true));
        });
        col.Item().PaddingTop(4).Text(
                $"Favorites are the genres most often given a ⭐, among those with at least {AlbumAnalytics.MinAlbums} albums heard; " +
                "ties go to the one with more albums. Bars split each genre by rating, ⭐ first and ✓ last.")
            .FontSize(7.5f).FontColor(Muted);

        if (report.AlbumsWithoutGenre > 0)
            col.Item().PaddingTop(2).Text($"{report.AlbumsWithoutGenre} heard albums have no genre yet and aren't in the genre lists.")
                .FontSize(7.5f).FontColor(Muted);
    }

    // ---------- Your additions, and the shortlist ----------

    private static void Additions(ColumnDescriptor col, AlbumAnalytics.Report report)
    {
        var a = report.Additions;
        col.Item().Element(c => Tiles(c,
            (a.Count.ToString(), "ALBUMS ADDED", $"beside {report.MustHearCount} kept from the 1001"),
            (a.ShareSince2000.ToString("P0"), "FROM 2000 ON", $"against {a.The1001ShareSince2000:P0} of the 1001"),
            (a.TopArtist?.Artist ?? "—", "MOST ADDED ARTIST", a.TopArtist is { } t ? $"{t.Albums} albums" : ""),
            ($"{a.NewArtists} / {a.Artists}", "NEW ARTISTS", "with nothing on the 1001")));

        col.Item().PaddingTop(16).Row(row =>
        {
            row.RelativeItem().Element(c => DecadeComparison(c, a.Decades));
            row.ConstantItem(24);
            row.RelativeItem().Element(c => ArtistTable(c, a.TopArtists));
        });

        col.Item().PaddingTop(22).PaddingBottom(2)
            .Text($"The Shortlist — {report.ShortlistPending} Still Under Consideration · {report.ShortlistKept} Kept")
            .FontSize(13).Bold().FontColor(Accent);
        col.Item().PaddingTop(8).Row(row =>
        {
            row.RelativeItem().Element(c => BarTable(c, "By decade", report.ShortlistDecades, fill: false));
            row.ConstantItem(24);
            row.RelativeItem().Element(c => BarTable(c, "Top genres", report.ShortlistGenres, fill: false));
        });
    }

    /// <summary>Per decade, two bars: the share of your additions, and the 1001's share, for scale.</summary>
    private static void DecadeComparison(IContainer container, IReadOnlyList<AlbumAnalytics.DecadeShare> decades)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(3).Text("By decade, against the 1001").FontSize(11).SemiBold().FontColor(Ink);
            col.Item().PaddingBottom(6).Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Muted));
                t.Span("■ ").FontColor(YoursInk);
                t.Span("Your additions    ");
                t.Span("■ ").FontColor(The1001Ink);
                t.Span("The 1001 — share of each list");
            });

            if (decades.Count == 0) { col.Item().Text("No data yet.").FontSize(8.5f).Italic().FontColor(Muted); return; }
            double max = Math.Max(0.01, decades.Max(d => Math.Max(d.AddedShare, d.The1001Share)));

            foreach (var d in decades)
            {
                col.Item().PaddingBottom(5).Row(r =>
                {
                    r.ConstantItem(44).Text(d.Label).FontSize(8.5f).FontColor(Ink);
                    r.RelativeItem().Column(bars =>
                    {
                        bars.Item().Height(6).Element(c => Bar(c, d.AddedShare / max, YoursInk));
                        bars.Item().PaddingTop(1).Height(4).Element(c => Bar(c, d.The1001Share / max, The1001Ink));
                    });
                    r.ConstantItem(70).AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(8.5f).FontColor(Muted));
                        t.Span($"{d.AddedShare:P0}").FontColor(Ink);
                        t.Span($"  vs {d.The1001Share:P0}");
                    });
                });
            }
        });
    }

    private static void ArtistTable(IContainer container, IReadOnlyList<AlbumAnalytics.ArtistCount> artists)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(3).Text("Most added artists").FontSize(11).SemiBold().FontColor(Ink);
            col.Item().PaddingBottom(6).Text("Artists with two or more albums added · * also on the 1001")
                .FontSize(7.5f).FontColor(Muted);

            if (artists.Count == 0) { col.Item().Text("No artist twice yet.").FontSize(8.5f).Italic().FontColor(Muted); return; }
            double max = artists.Max(a => a.Albums);
            foreach (var a in artists)
            {
                col.Item().PaddingBottom(5).Row(r =>
                {
                    r.ConstantItem(110).Text(a.Artist + (a.OnThe1001 ? " *" : "")).FontSize(8.5f).FontColor(Ink).ClampLines(1);
                    r.RelativeItem().Height(9).Element(c => Bar(c, a.Albums / max, YoursInk));
                    r.ConstantItem(30).AlignRight().Text(a.Albums.ToString()).FontSize(8.5f).FontColor(Muted);
                });
            }
        });
    }

    // ---------- Pieces ----------

    private static void Tiles(IContainer container, params (string Value, string Label, string Detail)[] tiles)
    {
        container.Row(row =>
        {
            foreach (var (value, label, detail) in tiles)
            {
                row.RelativeItem().Padding(2).Border(1).BorderColor(Rule).Padding(10).Column(c =>
                {
                    c.Item().Text(label).FontSize(7.5f).FontColor(Muted);
                    c.Item().PaddingTop(2).Text(value).FontSize(15).Bold().FontColor(Accent).ClampLines(1);
                    c.Item().PaddingTop(1).Text(detail).FontSize(7.5f).FontColor(Muted);
                });
            }
        });
    }

    private static string Detail(AlbumAnalytics.Bucket? b) =>
        b is null ? $"needs {AlbumAnalytics.MinAlbums}+ albums heard" : $"{b.Mix.Starred} of {b.Count} heard got a ⭐ ({b.Mix.StarShare:P0})";

    private static string Pct(int part, int whole) => whole == 0 ? "0%" : ((double)part / whole).ToString("P0");

    private static void MixBar(IContainer container, AlbumAnalytics.RatingMix mix)
    {
        if (mix.Heard == 0) return;
        container.Column(col =>
        {
            col.Item().Height(12).Row(bar =>
            {
                for (int i = 0; i < AlbumAnalytics.Marks.Length; i++)
                {
                    int n = mix.CountOf(AlbumAnalytics.Marks[i]);
                    if (n > 0) bar.RelativeItem(n).Background(MarkInk[i]);
                }
            });
            col.Item().PaddingTop(5).Row(row =>
            {
                for (int i = 0; i < AlbumAnalytics.Marks.Length; i++)
                {
                    int n = mix.CountOf(AlbumAnalytics.Marks[i]);
                    int mark = i;
                    row.RelativeItem().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                        t.Span("■ ").FontColor(MarkInk[mark]);
                        t.Span($"{AlbumAnalytics.Marks[mark]} {MarkLabels[mark]}\n");
                        t.Span($"{n}").FontSize(10).Bold().FontColor(Ink);
                        t.Span($"  {(double)n / mix.Heard:P0}");
                    });
                }
            });
        });
    }

    /// <summary>A bar filled to <paramref name="fraction"/> of its track.</summary>
    private static void Bar(IContainer container, double fraction, string color)
    {
        float f = (float)Math.Clamp(fraction, 0.01, 1.0);
        container.Row(bar =>
        {
            bar.RelativeItem(f).Background(color);
            if (f < 1f) bar.RelativeItem(1f - f);
        });
    }

    /// <summary>
    /// A label, a bar and figures per bucket. Unless <paramref name="fill"/> is set, each bar is
    /// sized by its count against the largest; filled bars are all full width, so what's compared
    /// is how each was rated. Heard buckets are split by mark either way and show their ⭐ rate.
    /// </summary>
    private static void BarTable(IContainer container, string title, IReadOnlyList<AlbumAnalytics.Bucket> buckets, bool fill)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(6).Text(title).FontSize(11).SemiBold().FontColor(Ink);

            if (buckets.Count == 0)
            {
                col.Item().Text("No data yet.").FontSize(8.5f).Italic().FontColor(Muted);
                return;
            }

            double max = Math.Max(1, buckets.Max(b => b.Count));

            foreach (var b in buckets)
            {
                float fraction = fill ? 1f : (float)Math.Clamp(b.Count / max, 0.01, 1.0);

                col.Item().PaddingBottom(5).Row(r =>
                {
                    r.ConstantItem(96).Text(b.Label).FontSize(8.5f).FontColor(Ink).ClampLines(1);
                    r.RelativeItem().Height(9).Background(BarTrack).Row(bar =>
                    {
                        bar.RelativeItem(fraction).Row(part =>
                        {
                            if (b.Mix.Heard == 0) { part.RelativeItem().Background(MarkInk[0]); return; }
                            for (int i = 0; i < AlbumAnalytics.Marks.Length; i++)
                            {
                                int n = b.Mix.CountOf(AlbumAnalytics.Marks[i]);
                                if (n > 0) part.RelativeItem(n).Background(MarkInk[i]);
                            }
                        });
                        if (fraction < 1f) bar.RelativeItem(1f - fraction);
                    });
                    r.ConstantItem(66).AlignRight()
                        .Text(b.Mix.Heard > 0 ? $"{b.Count} · {b.Mix.StarShare:P0} ⭐" : b.Count.ToString())
                        .FontSize(8.5f).FontColor(Muted);
                });
            }
        });
    }
}
