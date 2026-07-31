using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace _1001AlbumHelper;

/// <summary>
/// Renders the Analytics window's stat tiles and bucket breakdowns as a single-page-ish PDF, in
/// the same visual language as <see cref="PdfExporter"/> — a snapshot of taste patterns you can
/// keep or share, rather than a live-only view.
/// </summary>
public static class AnalyticsPdfExporter
{
    private const string Ink = "#1b1714";
    private const string Muted = "#7a6a5c";
    private const string Rule = "#d9cec3";
    private const string Accent = "#8a6318";
    private const string BarColor = "#c9a227";
    private const string BarTrack = "#efe9e2";

    public static void Write(
        string path,
        AlbumAnalytics.TasteSummary summary,
        IReadOnlyList<AlbumAnalytics.Bucket> decades,
        IReadOnlyList<AlbumAnalytics.Bucket> genres,
        IReadOnlyList<AlbumAnalytics.Bucket> ratingDistribution,
        IReadOnlyList<AlbumAnalytics.Bucket> bestGenres,
        int shortlistPending,
        IReadOnlyList<AlbumAnalytics.Bucket> shortlistDecades,
        IReadOnlyList<AlbumAnalytics.Bucket> shortlistGenres)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Ink));

                page.Header().Element(h => Header(h, summary));
                page.Content().Element(c => Content(
                    c, summary, decades, genres, ratingDistribution, bestGenres,
                    shortlistPending, shortlistDecades, shortlistGenres));
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

    private static void Header(IContainer container, AlbumAnalytics.TasteSummary summary)
    {
        container.PaddingBottom(14).Column(col =>
        {
            col.Item().Text("Taste Patterns Report").FontSize(19).Bold().FontColor(Ink);
            col.Item().PaddingTop(3)
                .Text($"{summary.RatedAlbums} of {summary.TotalAlbums} albums rated · " +
                      $"compiled {DateTime.Now:d MMMM yyyy}")
                .FontSize(9).FontColor(Muted);
            col.Item().PaddingTop(9).LineHorizontal(1).LineColor(Rule);
        });
    }

    private static void Content(
        IContainer container,
        AlbumAnalytics.TasteSummary summary,
        IReadOnlyList<AlbumAnalytics.Bucket> decades,
        IReadOnlyList<AlbumAnalytics.Bucket> genres,
        IReadOnlyList<AlbumAnalytics.Bucket> ratingDistribution,
        IReadOnlyList<AlbumAnalytics.Bucket> bestGenres,
        int shortlistPending,
        IReadOnlyList<AlbumAnalytics.Bucket> shortlistDecades,
        IReadOnlyList<AlbumAnalytics.Bucket> shortlistGenres)
    {
        container.Column(col =>
        {
            col.Item().Element(c => StatTiles(c, summary));

            col.Item().PaddingTop(18).PaddingBottom(2)
                .Text("Your Ratings — The 1001 List").FontSize(13).Bold().FontColor(Accent);

            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Element(c => BarTable(c, "By decade", decades));
                row.ConstantItem(24);
                row.RelativeItem().Element(c => BarTable(c, "Top genres", Top(genres, 10)));
            });

            col.Item().PaddingTop(14).Row(row =>
            {
                row.RelativeItem().Element(c => BarTable(c, "Rating distribution", ratingDistribution));
                row.ConstantItem(24);
                row.RelativeItem().Element(c =>
                    BarTable(c, "Best genres (avg. score, min. 3 albums)", Top(bestGenres, 10), showScore: true));
            });

            col.Item().PaddingTop(20).PaddingBottom(2)
                .Text($"Your Shortlist — Potential Replacements ({shortlistPending} pending)")
                .FontSize(13).Bold().FontColor(Accent);

            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Element(c => BarTable(c, "By decade", shortlistDecades));
                row.ConstantItem(24);
                row.RelativeItem().Element(c => BarTable(c, "Top genres", Top(shortlistGenres, 10)));
            });
        });
    }

    private static List<AlbumAnalytics.Bucket> Top(IReadOnlyList<AlbumAnalytics.Bucket> buckets, int n) =>
        buckets.Take(n).ToList();

    private static void StatTiles(IContainer container, AlbumAnalytics.TasteSummary summary)
    {
        container.Row(row =>
        {
            void Tile(string value, string label)
            {
                row.RelativeItem().Padding(2).Border(1).BorderColor(Rule).Padding(10).Column(c =>
                {
                    c.Item().Text(value).FontSize(16).Bold().FontColor(Accent);
                    c.Item().PaddingTop(2).Text(label).FontSize(7.5f).FontColor(Muted);
                });
            }

            Tile($"{summary.RatedAlbums}/{summary.TotalAlbums}", "ALBUMS RATED");
            Tile(summary.RatedAlbums > 0 ? summary.AverageScore.ToString("0.00") : "—", "AVERAGE SCORE (OF 4)");
            Tile(summary.FavoriteDecade ?? "—", "FAVORITE DECADE");
            Tile(summary.FavoriteGenre ?? "—", "FAVORITE GENRE");
        });
    }

    /// <summary>
    /// A label + a filled horizontal bar (proportional to the largest count, or to a 0–4 score
    /// scale when <paramref name="showScore"/> is set) + the raw number, one row per bucket.
    /// </summary>
    private static void BarTable(
        IContainer container, string title, IReadOnlyList<AlbumAnalytics.Bucket> buckets, bool showScore = false)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(6).Text(title).FontSize(11).SemiBold().FontColor(Ink);

            if (buckets.Count == 0)
            {
                col.Item().Text("No data yet.").FontSize(8.5f).Italic().FontColor(Muted);
                return;
            }

            double max = showScore ? 4.0 : buckets.Max(b => b.Count);
            if (max <= 0) max = 1;

            foreach (var b in buckets)
            {
                double raw = showScore ? b.AverageScore : b.Count;
                float fraction = (float)Math.Clamp(raw / max, 0.01, 1.0);

                col.Item().PaddingBottom(5).Row(r =>
                {
                    r.ConstantItem(92).Text(b.Label).FontSize(8.5f).FontColor(Ink);
                    r.RelativeItem().Height(9).Background(BarTrack).Row(bar =>
                    {
                        bar.RelativeItem(fraction).Background(BarColor);
                        if (fraction < 1f) bar.RelativeItem(1f - fraction);
                    });
                    r.ConstantItem(38).AlignRight()
                        .Text(showScore ? b.AverageScore.ToString("0.0") : b.Count.ToString())
                        .FontSize(8.5f).FontColor(Muted);
                });
            }
        });
    }
}
