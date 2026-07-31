using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace _1001AlbumHelper;

public partial class AnalyticsWindow : Window
{
    public AnalyticsWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        StatusText.Text = "";
        SummaryText.Text = "Loading…";

        var session = await Operations.OpenRatingSessionAsync();
        if (session is null)
        {
            SummaryText.Text = "Couldn't load the album list — check the log for what went wrong.";
            return;
        }

        // "Rated" means an actual ⭐/👍/👎/❌ score — a "✓" (listened, not yet scored) doesn't
        // count, so this matches what every chart below actually plots.
        var rated = session.AllAlbums.Where(a => AlbumAnalytics.ScoreFor(a.Rating) is not null).ToList();
        int withGenre = rated.Count(a => a.Genre.Length > 0);

        SummaryText.Text = $"{rated.Count} rated album(s) — {withGenre} have a genre" +
            (withGenre < rated.Count
                ? $" ({rated.Count - withGenre} still need `dotnet run -- backfill-genres`)."
                : ".");

        var summary = AlbumAnalytics.Summarize(session.AllAlbums);
        CompletionValue.Text = $"{summary.RatedAlbums}/{summary.TotalAlbums}";
        AverageScoreValue.Text = summary.RatedAlbums > 0 ? summary.AverageScore.ToString("0.00") : "—";
        FavoriteDecadeValue.Text = summary.FavoriteDecade ?? "—";
        FavoriteGenreValue.Text = summary.FavoriteGenre ?? "—";

        var decades = AlbumAnalytics.DecadeBreakdown(rated);
        DecadeChart.Series = new ISeries[]
        {
            new ColumnSeries<int> { Values = decades.Select(b => b.Count).ToArray(), Name = "Albums" },
        };
        DecadeChart.XAxes = new[] { new Axis { Labels = decades.Select(b => b.Label).ToArray() } };

        var genres = AlbumAnalytics.GenreBreakdown(rated).Take(10).ToList();
        GenreChart.Series = new ISeries[]
        {
            new ColumnSeries<int> { Values = genres.Select(b => b.Count).ToArray(), Name = "Albums" },
        };
        GenreChart.XAxes = new[] { GenreAxis(genres) };

        // All four ratings always show, even ones with a zero count, so the axis reads
        // consistently ⭐ → ❌ every time (a PieChart here silently rendered nothing — this
        // ColumnSeries approach matches every other working chart in the window).
        var ratingDistribution = AlbumAnalytics.RatingDistribution(rated);
        RatingChart.Series = new ISeries[]
        {
            new ColumnSeries<int> { Values = ratingDistribution.Select(b => b.Count).ToArray(), Name = "Albums" },
        };
        RatingChart.XAxes = new[] { new Axis { Labels = ratingDistribution.Select(b => b.Label).ToArray() } };

        var bestGenres = AlbumAnalytics.BestGenres(rated).Take(10).ToList();
        BestGenresChart.Series = new ISeries[]
        {
            new ColumnSeries<double>
            {
                Values = bestGenres.Select(b => Math.Round(b.AverageScore, 2)).ToArray(),
                Name = "Avg. score",
            },
        };
        BestGenresChart.XAxes = new[] { GenreAxis(bestGenres) };
        BestGenresChart.YAxes = new[] { new Axis { MinLimit = 0, MaxLimit = 4 } };

        // Best-effort: the local cache loads instantly; a Sheets pull (when configured) refreshes
        // it first so the shortlist chart reflects any edits made on another device.
        var repo = CandidateRepository.Create();
        try
        {
            var remote = await repo.PullAsync();
            if (remote is { Count: > 0 }) ReplacementCandidates.Save(remote);
        }
        catch { /* local cache is still a fine fallback */ }

        var candidates = ReplacementCandidates.Load();
        int pending = candidates.Count(c => c.Status == CandidateStatus.Pending);
        ShortlistSummaryText.Text = $"{pending} still under consideration.";

        var shortlistDecades = AlbumAnalytics.CandidateDecadeBreakdown(candidates);
        ShortlistDecadeChart.Series = new ISeries[]
        {
            new ColumnSeries<int> { Values = shortlistDecades.Select(b => b.Count).ToArray(), Name = "Albums" },
        };
        ShortlistDecadeChart.XAxes = new[] { new Axis { Labels = shortlistDecades.Select(b => b.Label).ToArray() } };

        var shortlistGenres = AlbumAnalytics.CandidateGenreBreakdown(candidates).Take(10).ToList();
        ShortlistGenreChart.Series = new ISeries[]
        {
            new ColumnSeries<int> { Values = shortlistGenres.Select(b => b.Count).ToArray(), Name = "Albums" },
        };
        ShortlistGenreChart.XAxes = new[] { GenreAxis(shortlistGenres) };
    }

    /// <summary>
    /// A genre axis can have up to 10 categories, which the default axis would otherwise thin out
    /// by skipping labels that'd overlap — force one per bar and angle them so they still fit.
    /// </summary>
    private static Axis GenreAxis(IEnumerable<AlbumAnalytics.Bucket> buckets) => new()
    {
        Labels = buckets.Select(b => b.Label).ToArray(),
        MinStep = 1,
        ForceStepToMin = true,
        LabelsRotation = -30,
        TextSize = 11,
    };

    private async void OnReload(object? sender, RoutedEventArgs e) => await LoadAsync();

    private async void OnExportPdf(object? sender, RoutedEventArgs e)
    {
        ExportPdfButton.IsEnabled = false;
        StatusText.Text = "Writing PDF…";
        try
        {
            var path = await Operations.ExportAnalyticsPdfAsync();
            StatusText.Text = path is not null
                ? $"Saved to {path}"
                : "Couldn't write the PDF — check the log for what went wrong.";
            if (path is not null) RevealInFinder(path);
        }
        finally
        {
            ExportPdfButton.IsEnabled = true;
        }
    }

    /// <summary>Opens a Finder window with the file selected.</summary>
    private static void RevealInFinder(string path)
    {
        try { System.Diagnostics.Process.Start("open", $"-R \"{path}\""); }
        catch { /* non-fatal: the status line already shows where the file went */ }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
