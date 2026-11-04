using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace _1001AlbumHelper;

/// <summary>
/// Taste patterns — the main window's Insights section, in three parts kept apart on purpose: the
/// 1001 (what you've heard and how you rated it), the albums you added, and the shortlist. The
/// numbers are one <see cref="AlbumAnalytics.Report"/>, the same one the PDFs draw; the charts are
/// plain Avalonia shapes drawn here, so they share the app's palette and type.
/// </summary>
public partial class InsightsPage : UserControl, IPage
{
    // The four scores as one gold ramp, ⭐ brightest — the order is the meaning, so the color
    // steps with it — then ✓ (heard, never scored) in a neutral gray, since it isn't a step on that
    // scale. Checked against the card surface: the ramp's lightness falls monotonically, every color
    // is ≥ 2:1 on #17120f, and neighbors are apart (OKLab ΔE 15, 16, 10, then 8.7 to the gray).
    // Same order as AlbumAnalytics.Marks.
    private static readonly IBrush[] MarkBrushes =
        new[] { "#f5c56a", "#c8913a", "#8f6229", "#6b4a2a", "#6a645e" }.Select(Brush.Parse).ToArray();

    private static readonly string[] MarkLabels =
        RatingSession.Choices.Select(c => c.Label).Append("Heard, not scored").ToArray();

    /// <summary>Your additions against the 1001 as a whole: the accent beside the neutral.</summary>
    private static readonly IBrush Yours = MarkBrushes[0];
    private static readonly IBrush The1001 = MarkBrushes[4];

    private static readonly IBrush Muted = Brush.Parse("#8b7969");
    private static readonly IBrush Ink = Brush.Parse("#f0e5da");

    private const double ColumnChartHeight = 220;

    public InsightsPage()
    {
        InitializeComponent();
    }

    private bool _loaded;
    private bool _loading;

    public async void OnShown()
    {
        if (_loaded) return;
        _loaded = true;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        ReloadButton.IsEnabled = false;
        SetStatus("");
        SummaryText.Text = "Loading…";
        try
        {
            var lists = await Operations.LoadAllListsAsync();
            if (lists.Error is not null)
            {
                SummaryText.Text = $"Couldn't load the lists — {lists.Error}";
                return;
            }
            if (lists.CachedAt is { } at)
                SetStatus($"Offline — these are the lists as they were at {at:MMM d, h:mm tt}.");

            var report = await Operations.BuildInsightsReportAsync(lists);
            ShowThe1001(report);
            ShowAdditions(report);
            ShowShortlist(report);
        }
        finally
        {
            _loading = false;
            ReloadButton.IsEnabled = true;
        }
    }

    // ---------- The 1001 ----------

    private void ShowThe1001(AlbumAnalytics.Report report)
    {
        var taste = report.Taste;
        var mix = taste.Mix;

        SummaryText.Text = "What you've heard on the 1001 and how you rated it, the albums you added to the finished list, and the shortlist.";
        The1001Section.IsVisible = true;
        The1001Hint.Text = mix.Heard == 0
            ? "Nothing heard yet — this fills in as you go."
            : $"Across the {mix.Heard} albums you've heard." +
              (report.AlbumsWithoutGenre > 0
                  ? $" {report.AlbumsWithoutGenre} have no genre yet, so they're not in the genre lists — Tools ▸ Fill in missing genres."
                  : "");

        double done = taste.TotalAlbums == 0 ? 0 : (double)taste.HeardAlbums / taste.TotalAlbums;
        The1001Tiles.Content = TileRow(
            Tile("HEARD", taste.HeardAlbums.ToString(), $"/ {taste.TotalAlbums}",
                $"{done:P0} of the list · {taste.TotalAlbums - taste.HeardAlbums} to go", progress: done),
            Tile("⭐ RATE", mix.Heard > 0 ? mix.StarShare.ToString("P0") : "—", null,
                mix.Heard > 0 ? $"{mix.Starred} of {mix.Heard} heard got a ⭐" : ""),
            Tile("FAVORITE GENRE", taste.FavoriteGenre?.Label ?? "—", null, Describe(taste.FavoriteGenre),
                tip: taste.FavoriteGenre is { } g ? Tip(g) : null),
            Tile("BEST DECADE", taste.FavoriteDecade?.Label ?? "—", null, Describe(taste.FavoriteDecade),
                tip: taste.FavoriteDecade is { } d ? Tip(d) : null));

        RatingMixHost.Content = Card("Everything you've heard",
            "A ✓ is an album you listened to and never scored. None of them got a ⭐, so they count as heard but not starred.",
            RatingMixChart(mix), Legend());

        DecadeHost.Content = Card("By decade", "Albums you've heard, split by rating",
            DecadeChart(report.Decades), Legend());
        MostHeardHost.Content = Card("Most heard genres", "Albums heard · share that got a ⭐",
            BarList(report.MostHeardGenres, ranked: false), Legend());

        FavoritesHost.Content = Card("Favorite genres",
            $"Most often a ⭐ · genres with {AlbumAnalytics.MinAlbums}+ albums heard",
            BarList(report.FavoriteGenres, ranked: true), Legend());
        LeastHost.Content = Card("Least favorite genres",
            $"Least often a ⭐ · genres with {AlbumAnalytics.MinAlbums}+ albums heard",
            BarList(report.LeastFavoriteGenres, ranked: true), Legend());
    }

    private static string Describe(AlbumAnalytics.Bucket? b) =>
        b is null
            ? $"Needs {AlbumAnalytics.MinAlbums}+ albums heard"
            : $"{b.Mix.Starred} of {b.Count} heard got a ⭐ ({b.Mix.StarShare:P0})";

    // ---------- The albums you added ----------

    private void ShowAdditions(AlbumAnalytics.Report report)
    {
        var a = report.Additions;
        AdditionsSection.IsVisible = true;

        AdditionsTiles.Content = TileRow(
            Tile("ALBUMS ADDED", a.Count.ToString(), null, $"beside {report.MustHearCount} kept from the 1001"),
            Tile("FROM 2000 ON", a.Count > 0 ? a.ShareSince2000.ToString("P0") : "—", null,
                $"of your additions · {a.The1001ShareSince2000:P0} of the 1001"),
            Tile("MOST ADDED ARTIST", a.TopArtist?.Artist ?? "—", null,
                a.TopArtist is { } t ? $"{t.Albums} albums" + (t.OnThe1001 ? " · also on the 1001" : "") : "No artist twice yet",
                tip: a.TopArtist?.Artist),
            Tile("NEW ARTISTS", a.NewArtists.ToString(), $"/ {a.Artists}", "with nothing on the 1001"));

        var key = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        key.Children.Add(KeyItem(Yours, "Your additions"));
        key.Children.Add(KeyItem(The1001, "The 1001"));
        AdditionsDecadeHost.Content = Card("By decade, against the 1001",
            "Each list's share by decade — where you went beyond the book",
            DecadeComparison(a.Decades), key);

        AdditionsArtistHost.Content = Card("Most added artists", "Artists with two or more albums added",
            ArtistList(a.TopArtists), null);
    }

    // ---------- The shortlist ----------

    private void ShowShortlist(AlbumAnalytics.Report report)
    {
        ShortlistSection.IsVisible = true;
        ShortlistHint.Text = $"{report.ShortlistPending} potential replacements still under consideration · {report.ShortlistKept} kept so far.";
        ShortlistDecadeHost.Content = Card("By decade", "Still under consideration",
            DecadeChart(report.ShortlistDecades), null);
        ShortlistGenreHost.Content = Card("Top genres", "Still under consideration",
            BarList(report.ShortlistGenres, ranked: false), null);
    }

    // ---------- Building blocks ----------

    private static Grid TileRow(params Control[] tiles)
    {
        var row = new Grid();
        for (int i = 0; i < tiles.Length; i++)
        {
            if (i > 0) row.ColumnDefinitions.Add(new ColumnDefinition(12, GridUnitType.Pixel));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(tiles[i], i * 2);
            row.Children.Add(tiles[i]);
        }
        return row;
    }

    private static Control Tile(string label, string value, string? unit, string detail,
                                double? progress = null, string? tip = null)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = label, Classes = { "tileLabel" } });

        var big = new TextBlock { Text = value, Classes = { "tileValue" } };
        if (tip is not null) ToolTip.SetTip(big, tip);
        if (unit is null) stack.Children.Add(big);
        else
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(big);
            line.Children.Add(new TextBlock { Text = unit, Classes = { "tileUnit" } });
            stack.Children.Add(line);
        }

        if (progress is { } p)
        {
            var bar = new Grid { Height = 5, Margin = new Thickness(0, 2, 0, 3), ColumnDefinitions = Split(p, 1 - p) };
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Yours });
            var rest = new Border { CornerRadius = new CornerRadius(3), Background = Brush.Parse("#2c231c") };
            Grid.SetColumn(rest, 1);
            bar.Children.Add(rest);
            stack.Children.Add(bar);
        }

        stack.Children.Add(new TextBlock { Text = detail, Classes = { "tileDetail" } });
        return new Border { Classes = { "card" }, Child = stack };
    }

    private static Control Card(string title, string? hint, Control body, Control? key)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 14) };
        var titles = new StackPanel { Spacing = 3 };
        titles.Children.Add(new TextBlock { Text = title, Classes = { "cardTitle" } });
        if (hint is not null) titles.Children.Add(new TextBlock { Text = hint, Classes = { "cardHint" } });
        header.Children.Add(titles);
        if (key is not null)
        {
            key.VerticalAlignment = VerticalAlignment.Top;
            key.Margin = new Thickness(12, 2, 0, 0);
            Grid.SetColumn(key, 1);
            header.Children.Add(key);
        }

        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(body);
        return new Border { Classes = { "card" }, Child = panel };
    }

    /// <summary>A swatch and symbol per mark; the full names are in the "Everything you've heard" card.</summary>
    private static StackPanel Legend()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        for (int i = 0; i < AlbumAnalytics.Marks.Length; i++)
        {
            var item = KeyItem(MarkBrushes[i], AlbumAnalytics.Marks[i]);
            ToolTip.SetTip(item, MarkLabels[i]);
            row.Children.Add(item);
        }
        return row;
    }

    private static StackPanel KeyItem(IBrush brush, string text)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        item.Children.Add(Swatch(brush));
        item.Children.Add(new TextBlock { Text = text, FontSize = 11, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
        return item;
    }

    private static Border Swatch(IBrush brush) => new()
    {
        Width = 9, Height = 9, CornerRadius = new CornerRadius(2),
        Background = brush, VerticalAlignment = VerticalAlignment.Center,
    };

    private static Control Empty(string text = "Nothing here yet.") =>
        new TextBlock { Text = text, FontSize = 12, Foreground = Muted, FontStyle = FontStyle.Italic };

    /// <summary>
    /// Every heard album as the parts of one bar — read left to right, ⭐ to ❌, then ✓ — with each
    /// mark's count and share underneath.
    /// </summary>
    private static Control RatingMixChart(AlbumAnalytics.RatingMix mix)
    {
        if (mix.Heard == 0) return Empty();

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Segments(mix, horizontal: true, thickness: 16));

        var counts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*") };
        for (int i = 0; i < AlbumAnalytics.Marks.Length; i++)
        {
            string symbol = AlbumAnalytics.Marks[i];
            int n = mix.CountOf(symbol);
            var cell = new StackPanel { Spacing = 2 };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            head.Children.Add(Swatch(MarkBrushes[i]));
            head.Children.Add(new TextBlock { Text = $"{symbol}  {MarkLabels[i]}", FontSize = 11.5, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis });
            cell.Children.Add(head);
            var figure = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(15, 0, 0, 0) };
            figure.Children.Add(new TextBlock { Text = n.ToString(), FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Ink });
            figure.Children.Add(new TextBlock
            {
                Text = $"{(double)n / mix.Heard:P0}", FontSize = 11.5, Foreground = Muted,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3),
            });
            cell.Children.Add(figure);
            Grid.SetColumn(cell, i);
            counts.Children.Add(cell);
        }
        panel.Children.Add(counts);
        return panel;
    }

    /// <summary>
    /// A bar split by mark, ⭐ first (left, or at the baseline when standing up) and ✓ last, with a
    /// 2px gap between parts. A mix with no ratings — the shortlist's — draws as one plain bar.
    /// </summary>
    private static Control Segments(AlbumAnalytics.RatingMix mix, bool horizontal, double thickness)
    {
        var parts = mix.Heard == 0
            ? new List<(int Count, IBrush Brush)> { (1, Yours) }
            : AlbumAnalytics.Marks
                .Select((m, i) => (Count: mix.CountOf(m), Brush: MarkBrushes[i]))
                .Where(p => p.Count > 0)
                .ToList();
        if (!horizontal) parts.Reverse(); // top to bottom, so ⭐ sits on the baseline

        var grid = new Grid();
        for (int i = 0; i < parts.Count; i++)
        {
            var length = new GridLength(parts[i].Count, GridUnitType.Star);
            bool last = i == parts.Count - 1;
            var seg = new Border { Background = parts[i].Brush, CornerRadius = new CornerRadius(3) };
            if (horizontal)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(length));
                seg.Height = thickness;
                if (!last) seg.Margin = new Thickness(0, 0, 2, 0);
                Grid.SetColumn(seg, i);
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition(length));
                seg.Width = thickness;
                if (!last) seg.Margin = new Thickness(0, 0, 0, 2);
                Grid.SetRow(seg, i);
            }
            grid.Children.Add(seg);
        }
        return grid;
    }

    /// <summary>A column chart's frame: one star column per item, a recessive baseline, labels under.</summary>
    private static Grid ColumnFrame(int columns)
    {
        var grid = new Grid { Height = ColumnChartHeight + 44 };
        for (int i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var axis = new Border
        {
            Height = 1, Background = Brush.Parse("#2c231c"), VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 26),
        };
        Grid.SetColumnSpan(axis, Math.Max(1, columns));
        grid.Children.Add(axis);
        return grid;
    }

    /// <summary>One labeled column of a column chart, hit-testable for its tooltip.</summary>
    private static void AddColumn(Grid frame, int index, string label, Control bars, string tip)
    {
        var column = new DockPanel { Background = Brushes.Transparent };
        var text = new TextBlock { Text = ShortDecade(label), Classes = { "axis" }, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(text, Dock.Bottom);
        column.Children.Add(text);
        bars.VerticalAlignment = VerticalAlignment.Bottom;
        column.Children.Add(bars);
        ToolTip.SetTip(column, tip);
        Grid.SetColumn(column, index);
        frame.Children.Add(column);
    }

    /// <summary>One column per decade, height by count, split by mark; the count on top, details on hover.</summary>
    private static Control DecadeChart(IReadOnlyList<AlbumAnalytics.Bucket> decades)
    {
        if (decades.Count == 0) return Empty();
        double max = decades.Max(d => d.Count);

        var frame = ColumnFrame(decades.Count);
        for (int i = 0; i < decades.Count; i++)
        {
            var d = decades[i];
            var bar = Segments(d.Mix, horizontal: false, thickness: 30);
            bar.Height = Math.Max(3, ColumnChartHeight * d.Count / max);
            bar.HorizontalAlignment = HorizontalAlignment.Center;

            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock { Text = d.Count.ToString(), Classes = { "axis" }, Foreground = Brush.Parse("#cbbdb0") });
            stack.Children.Add(bar);
            AddColumn(frame, i, d.Label, stack, Tip(d));
        }
        return frame;
    }

    /// <summary>
    /// Per decade, your additions' share beside the 1001's, as a pair of columns. Shares rather than
    /// counts, since the two lists are different sizes; the bright one is labeled.
    /// </summary>
    private static Control DecadeComparison(IReadOnlyList<AlbumAnalytics.DecadeShare> decades)
    {
        if (decades.Count == 0) return Empty();
        double max = Math.Max(0.01, decades.Max(d => Math.Max(d.AddedShare, d.The1001Share)));

        var frame = ColumnFrame(decades.Count);
        for (int i = 0; i < decades.Count; i++)
        {
            var d = decades[i];
            var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
            pair.Children.Add(ShareColumn(d.AddedShare / max, Yours, d.Added > 0 ? $"{d.AddedShare:P0}" : ""));
            pair.Children.Add(ShareColumn(d.The1001Share / max, The1001, ""));

            AddColumn(frame, i, d.Label, pair,
                $"{d.Label}\nYour additions: {d.Added} ({d.AddedShare:P0})\nThe 1001: {d.InThe1001} ({d.The1001Share:P0})");
        }
        return frame;
    }

    private static Control ShareColumn(double fraction, IBrush brush, string label)
    {
        var stack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Bottom };
        stack.Children.Add(new TextBlock { Text = label, Classes = { "axis" }, Foreground = Brush.Parse("#cbbdb0"), MinHeight = 14 });
        stack.Children.Add(new Border
        {
            Width = 16, Height = fraction <= 0 ? 0 : Math.Max(3, ColumnChartHeight * fraction),
            Background = brush, CornerRadius = new CornerRadius(3, 3, 1, 1),
        });
        return stack;
    }

    /// <summary>"1970s" → "’70s"; the 1900s and 2000s keep the century, so they can't be misread.</summary>
    private static string ShortDecade(string label) =>
        label.Length == 5 && label.StartsWith("19") && label[2] != '0' ? $"’{label[2..]}" : label;

    /// <summary>
    /// A row per bucket: rank (when <paramref name="ranked"/>), name, bar, figures. Ranked lists
    /// fill every bar and compare how each was rated; the others size bars by album count. Heard
    /// buckets show their ⭐ rate beside the count.
    /// </summary>
    private static Control BarList(IReadOnlyList<AlbumAnalytics.Bucket> buckets, bool ranked)
    {
        if (buckets.Count == 0)
            return Empty(ranked ? $"No genre has {AlbumAnalytics.MinAlbums} albums heard yet." : "Nothing here yet.");

        double max = buckets.Max(b => b.Count);
        bool rated = buckets.Any(b => b.Mix.Heard > 0);

        var list = new StackPanel { Spacing = 7 };
        for (int i = 0; i < buckets.Count; i++)
        {
            var b = buckets[i];
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(ranked ? "22,140,*,66,48" : "140,*,40" + (rated ? ",48" : "")),
                Background = Brushes.Transparent, // hit-testable for the tooltip
                Height = 22,
            };
            int col = 0;
            void Add(Control c) { Grid.SetColumn(c, col++); row.Children.Add(c); }

            if (ranked) Add(new TextBlock { Text = (i + 1).ToString(), Classes = { "rowMuted" }, HorizontalAlignment = HorizontalAlignment.Left });
            Add(new TextBlock { Text = b.Label, Classes = { "rowLabel" }, Margin = new Thickness(0, 0, 10, 0) });

            var bar = Segments(b.Mix, horizontal: true, thickness: 10);
            bar.VerticalAlignment = VerticalAlignment.Center;
            var track = new Grid { ColumnDefinitions = ranked ? Split(1, 0) : Split(b.Count, max - b.Count) };
            track.Children.Add(bar);
            Add(track);

            if (ranked)
            {
                Add(new TextBlock { Text = $"{b.Count} heard", Classes = { "rowMuted" } });
                Add(new TextBlock { Text = b.Mix.StarShare.ToString("P0"), Classes = { "rowValue" } });
            }
            else
            {
                Add(new TextBlock { Text = b.Count.ToString(), Classes = { "rowValue" } });
                if (rated) Add(new TextBlock { Text = b.Mix.StarShare.ToString("P0"), Classes = { "rowMuted" } });
            }

            ToolTip.SetTip(row, Tip(b));
            list.Children.Add(row);
        }
        return list;
    }

    /// <summary>A row per artist: name, bar by albums added, count, and whether the 1001 has them too.</summary>
    private static Control ArtistList(IReadOnlyList<AlbumAnalytics.ArtistCount> artists)
    {
        if (artists.Count == 0) return Empty("No artist added twice yet.");
        double max = artists.Max(a => a.Albums);

        var list = new StackPanel { Spacing = 7 };
        foreach (var a in artists)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*,36,104"), Background = Brushes.Transparent, Height = 22 };
            row.Children.Add(new TextBlock { Text = a.Artist, Classes = { "rowLabel" }, Margin = new Thickness(0, 0, 10, 0) });

            var track = new Grid { ColumnDefinitions = Split(a.Albums, max - a.Albums) };
            track.Children.Add(new Border { Height = 10, CornerRadius = new CornerRadius(3), Background = Yours, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(track, 1);
            row.Children.Add(track);

            var count = new TextBlock { Text = a.Albums.ToString(), Classes = { "rowValue" } };
            Grid.SetColumn(count, 2);
            row.Children.Add(count);

            if (a.OnThe1001)
            {
                var tag = new TextBlock { Text = "also on the 1001", Classes = { "rowMuted" }, FontSize = 10.5 };
                Grid.SetColumn(tag, 3);
                row.Children.Add(tag);
            }

            ToolTip.SetTip(row, $"{a.Artist} — {a.Albums} albums added" + (a.OnThe1001 ? "; the 1001 has them too" : "; not on the 1001 at all"));
            list.Children.Add(row);
        }
        return list;
    }

    /// <summary>Two star columns in the given proportions — a filled part and the rest.</summary>
    private static ColumnDefinitions Split(double filled, double rest) => new()
    {
        new ColumnDefinition(new GridLength(Math.Max(0, filled), GridUnitType.Star)),
        new ColumnDefinition(new GridLength(Math.Max(0, rest), GridUnitType.Star)),
    };

    private static string Tip(AlbumAnalytics.Bucket b)
    {
        string albums = $"{b.Count} album{(b.Count == 1 ? "" : "s")}";
        if (b.Mix.Heard == 0) return $"{b.Label} — {albums}";
        var m = b.Mix;
        return $"{b.Label} — {albums} heard\n" +
               $"⭐ {m.Starred}   👍 {m.Liked}   👎 {m.Disliked}   ❌ {m.Trash}   ✓ {m.Listened}\n" +
               $"{m.StarShare:P0} got a ⭐" +
               (m.Scored > 0 ? $" · average {m.AverageScore:0.00} across the {m.Scored} scored" : "");
    }

    // ---------- Actions ----------

    private void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusText.IsVisible = text.Length > 0;
    }

    private async void OnReload(object? sender, RoutedEventArgs e) => await LoadAsync();

    private async void OnExportPdf(object? sender, RoutedEventArgs e)
    {
        ExportPdfButton.IsEnabled = false;
        SetStatus("Writing PDF…");
        try
        {
            var path = await Operations.ExportAnalyticsPdfAsync();
            SetStatus(path is not null
                ? $"Saved to {path}"
                : "Couldn't write the PDF — Tools ▸ Activity log says what went wrong.");
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
}
