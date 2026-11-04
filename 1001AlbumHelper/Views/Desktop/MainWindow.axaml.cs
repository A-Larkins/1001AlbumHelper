using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace _1001AlbumHelper;

/// <summary>
/// The app's one window: a sidebar of sections — Rate, Browse, Shortlist, Playlists, Export,
/// Insights, and Tools apart at the bottom — each shown in place rather than in a window of its own.
/// Rate is the home page. Export is the finished list and what the app is ultimately for; Tools holds
/// what's done now and then (the favorites backfill, the connection check) and the activity log.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly IBrush IdleDot = new SolidColorBrush(Color.Parse("#7c6d60"));
    private static readonly IBrush RunningDot = new SolidColorBrush(Color.Parse("#e6a54b"));
    private const string LogPlaceholder = "Ready. Output from anything the app does streams here live.";

    private enum Section { Rate, Browse, Shortlist, Playlists, Export, Insights, Tools }

    /// <summary>Pages built so far — each is made on first visit, then kept so it holds its place.</summary>
    private readonly Dictionary<Section, Control> _pages = new();
    private Section _current = Section.Rate;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DispatcherTimer _timer;
    private DateTime _startedAt;

    public MainWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) =>
        {
            var s = (DateTime.Now - _startedAt).TotalSeconds;
            ElapsedText.Text = s < 60 ? $"{s:0.0}s" : $"{(int)(s / 60)}m {(int)(s % 60)}s";
        };

        // Everything the app prints — sheet writes from the shortlist, adding an album — lands in
        // the activity log, as well as wherever stdout was going (the terminal, under dotnet run).
        Console.SetOut(new UiLogWriter(line => Dispatcher.UIThread.Post(() => AppendLine(line)), Console.Out));

        _pages[Section.Export] = ExportPage;
        _pages[Section.Tools] = ToolsPage;
        Opened += async (_, _) =>
        {
            // Dev aid, like the mobile preview's ALBUMHELPER_PREVIEW_TAB: open straight onto one
            // section (Rate, Browse, Shortlist, Playlists, More) to look at it without clicking.
            Navigate(Enum.TryParse<Section>(Environment.GetEnvironmentVariable("ALBUMHELPER_START_SECTION"),
                                            ignoreCase: true, out var start) ? start : Section.Rate);
            await RefreshSyncStatusAsync();
        };
        // Tunnel, so ⌘1–⌘5 are taken here before a page sees the key — the Rate page rates on bare
        // digits, and a shortcut must never reach it as one.
        AddHandler(KeyDownEvent, OnWindowKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        NavInsights.IsVisible = InsightsPageHost.Factory is not null;
    }

    /// <summary>The window's background, shared with <see cref="PageWindow"/> so a page looks the same in either.</summary>
    public static IBrush PageBackground() => new LinearGradientBrush
    {
        StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
        EndPoint = new Avalonia.RelativePoint(0.3, 1, Avalonia.RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#151020"), 0),
            new GradientStop(Color.Parse("#0e0b09"), 0.45),
            new GradientStop(Color.Parse("#0b0807"), 1),
        },
    };

    // ---------- Sections ----------

    private void OnNav(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<Section>(tag, out var section))
            Navigate(section);
    }

    /// <summary>⌘1–⌘7 jump between sections, in sidebar order.</summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Meta) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        Section? target = e.Key switch
        {
            Key.D1 => Section.Rate,
            Key.D2 => Section.Browse,
            Key.D3 => Section.Shortlist,
            Key.D4 => Section.Playlists,
            Key.D5 => Section.Export,
            Key.D6 when NavInsights.IsVisible => Section.Insights,
            Key.D7 => Section.Tools,
            _ => null,
        };
        if (target is { } t) { e.Handled = true; Navigate(t); }
    }

    private void Navigate(Section section)
    {
        if (!_pages.TryGetValue(section, out var page))
        {
            page = section switch
            {
                Section.Rate => new RatePage(RatingMode.NextUp),
                Section.Browse => new BrowsePage(),
                Section.Shortlist => new ShortlistPage(),
                Section.Insights => InsightsPageHost.Factory!(),
                _ => new PlaylistsPage(),
            };
            _pages[section] = page;
            PageHost.Children.Add(page);
        }

        _current = section;
        foreach (var (key, p) in _pages) p.IsVisible = key == section;
        NavRate.Classes.Set("on", section == Section.Rate);
        NavBrowse.Classes.Set("on", section == Section.Browse);
        NavShortlist.Classes.Set("on", section == Section.Shortlist);
        NavPlaylists.Classes.Set("on", section == Section.Playlists);
        NavExport.Classes.Set("on", section == Section.Export);
        NavInsights.Classes.Set("on", section == Section.Insights);
        NavTools.Classes.Set("on", section == Section.Tools);

        // Ratings made on the Rate page show up in Browse without re-reading the sheet.
        if (page is BrowsePage browse && _pages.TryGetValue(Section.Rate, out var rate))
            browse.ApplyRatings(((RatePage)rate).RatingsApplied);

        (page as IPage)?.OnShown();
        if (section == Section.Export) _ = RefreshExportCountsAsync();
    }

    /// <summary>
    /// Read-only check on open, so drift from browser edits is visible without being asked for.
    /// Failures are swallowed to a quiet label — the app must still work offline.
    /// </summary>
    private async Task RefreshSyncStatusAsync()
    {
        SyncDescText.Text = "Checking whether anything needs syncing…";

        var status = await Task.Run(() => Operations.CheckSyncStatusAsync(quiet: true));

        if (status.Error is not null)
        {
            SyncDescText.Text = "Reconciles both lists with the 1001 list — couldn't check right now.";
            SyncBadge.IsVisible = false;
            ExportDot.IsVisible = false;
            return;
        }

        if (status.NeedsSync)
        {
            SyncDescText.Text = status.Summary();
            SyncBadgeText.Text = "NEEDED";
            SyncBadge.IsVisible = true;
            ExportDot.IsVisible = true;
        }
        else
        {
            SyncDescText.Text = "Everything's in sync — nothing to reconcile.";
            SyncBadge.IsVisible = false;
            ExportDot.IsVisible = false;
        }
    }

    // ---------- Apple Music favorites ----------

    /// <summary>
    /// The read-only half of the favorites card: works out what a backfill would do and prints it,
    /// writing nothing. Deliberately terse next to the <c>favtest</c> command line, which names
    /// every album — this is a card on a home screen, and the counts are what decides whether to
    /// press it again.
    /// </summary>
    private static async Task<Operations.FavoritesBackfillPlan> PreviewFavoritesAsync()
    {
        Console.WriteLine("\n=== What a favorites backfill would do ===\n");

        var plan = await Operations.PlanFavoritesBackfillAsync();
        if (plan.Error is not null) { Console.WriteLine($"✗ {plan.Error}"); return plan; }

        Console.WriteLine($"Apple Music holds {plan.FavoritedInLibrary} favorited albums and "
                          + $"{plan.InPlaylist} in “{AppleMusic.FavoritesPlaylistName}”.");
        Console.WriteLine(plan.Summary);
        Console.WriteLine();
        foreach (var item in plan.Work) Console.WriteLine($"   {item.Intent,-20} {item.Album.Display}");
        Console.WriteLine($"\n{plan.Work.Count} album{(plan.Work.Count == 1 ? "" : "s")} to touch. "
                          + "Nothing has been written.");
        return plan;
    }

    /// <summary>
    /// Turns the favorites card into the "yes, do it" button once a preview has said what "it" is,
    /// and back again afterwards. The card is the confirmation: the backfill writes hundreds of
    /// changes into the real Apple Music library, and unlike everything else on this screen those
    /// can't be taken back from here — Apple's API has no remove (PROJECT.md §5).
    /// </summary>
    private void ArmFavorites(Operations.FavoritesBackfillPlan? plan)
    {
        bool armed = plan is { Error: null, Work.Count: > 0 };

        FavoritesButton.Tag = armed ? "favorites-backfill" : "favorites-check";
        FavoritesBadge.IsVisible = armed;

        if (armed)
        {
            FavoritesBadgeText.Text = "READY";
            FavoritesTitleText.Text = $"Backfill {plan!.Work.Count} albums now";
            FavoritesDescText.Text =
                $"{plan.ToFavorite} to favorite, {plan.ToAddToPlaylist} to add to "
                + $"“{AppleMusic.FavoritesPlaylistName}” — the log below lists them. Press to write it "
                + "to Apple Music. Expect a second or two per album, and note that the app can't undo "
                + "a playlist add afterwards.";
            return;
        }

        FavoritesTitleText.Text = "Backfill favorites";
        FavoritesDescText.Text = plan is { Error: null }
            ? "Apple Music already agrees with the lists — every album on them is favorited and in "
              + "“Almost forgot to listen to these today”. Press to check again."
            : "Put a heart on every album of the finished list that Apple Music hasn't already got "
              + "one on, and mirror the same albums into “Almost forgot to listen to these today”. "
              + "Press once to see what it would do — nothing is written until you press again.";
    }

    // ---------- Running an operation ----------
    private async void OnRun(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        if (!_gate.Wait(0)) return;

        SetRunning(true);
        LogBox.Text = "";
        _startedAt = DateTime.Now;
        _timer.Start();

        var sw = Stopwatch.StartNew();
        var writer = new UiLogWriter(line => Dispatcher.UIThread.Post(() => AppendLine(line)));
        bool ok = false;
        string? err = null;
        string? exportedPdf = null;
        Operations.FavoritesBackfillPlan? plan = null;

        await Task.Run(async () =>
        {
            var previousOut = Console.Out;
            Console.SetOut(writer);
            try
            {
                switch (action)
                {
                    case "sync-all": await Operations.SyncAllAsync(); break;
                    case "export-pdf": exportedPdf = await Operations.ExportPdfAsync(); break;
                    case "sync-check": await Operations.ListSheetTabsAsync(); break;
                    case "favorites-check": plan = await PreviewFavoritesAsync(); break;
                    case "favorites-backfill": await Operations.BackfillAppleMusicFavoritesAsync(); break;
                    case "backfill-genres": await Operations.BackfillGenresAsync(); break;
                }
                ok = true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                Console.WriteLine($"\n✗ Error: {ex.Message}");
            }
            finally
            {
                Console.SetOut(previousOut);
                writer.Flush();
            }
        });

        sw.Stop();

        // Queue the summary AFTER any log lines still sitting in the dispatcher queue.
        Dispatcher.UIThread.Post(() =>
        {
            _timer.Stop();
            AppendLine("");
            AppendLine(ok
                ? $"✓ Finished in {sw.Elapsed.TotalSeconds:0.0}s."
                : $"✗ Failed{(err != null ? ": " + err : ".")}");
            SetRunning(false);
            _gate.Release();

            // A sync changes exactly what the badge reports, so re-check once it's done.
            if (action == "sync-all") _ = RefreshSyncStatusAsync();

            // The favorites card is its own confirmation step — see ArmFavorites.
            if (action == "favorites-check") ArmFavorites(plan);
            else if (action == "favorites-backfill") ArmFavorites(null);

            // Reveal the finished PDF rather than making them hunt for it.
            if (exportedPdf is not null) { RevealInFinder(exportedPdf); ShowLastExport(exportedPdf); }

            // A sync changes what's on the finished list.
            if (action == "sync-all" && _current == Section.Export) _ = RefreshExportCountsAsync();
        });
    }

    private void SetRunning(bool on)
    {
        StatusText.Text = on ? "Running…" : "Idle";
        StatusDot.Fill = on ? RunningDot : IdleDot;
        CheckButton.IsEnabled = !on;
        GenresButton.IsEnabled = !on;
        SyncButton.IsEnabled = !on;
        ExportButton.IsEnabled = !on;
    }

    private void AppendLine(string line)
    {
        LogBox.Text = LogBox.Text?.Length > 0 ? LogBox.Text + "\n" + line : line;
        LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
    }

    private void OnClearLog(object? sender, RoutedEventArgs e) => LogBox.Text = LogPlaceholder;

    // ---------- Tools under More ----------

    // ---------- Export ----------

    /// <summary>The PDF the last export wrote, for "Show in Finder".</summary>
    private string? _lastExport;

    /// <summary>
    /// Fills in the Export page's three numbers from the sheet. Re-read on every visit (and after a
    /// sync), because rating a ⭐ or keeping a replacement elsewhere in the app changes them.
    /// </summary>
    private async Task RefreshExportCountsAsync()
    {
        var lists = await Task.Run(Operations.LoadAllListsAsync);
        if (lists.Error is not null)
        {
            ExportNoteText.Text = $"Couldn't read the lists — {lists.Error}";
            ExportNoteText.IsVisible = true;
            return;
        }
        ExportNoteText.Text = lists.CachedAt is { } since
            ? $"⚠️ Offline — these are the lists as they were at {since:MMM d, h:mm tt}. An export now would use that copy."
            : "";
        ExportNoteText.IsVisible = lists.CachedAt is not null;
        MustHearCountText.Text = lists.MustHear.Count.ToString();
        ReplacementsCountText.Text = lists.Replacements.Count.ToString();
        TotalCountText.Text = (lists.MustHear.Count + lists.Replacements.Count).ToString();
    }

    private void ShowLastExport(string path)
    {
        _lastExport = path;
        LastExportText.Text = $"Last export: {Path.GetFileName(path)}  ·  {DateTime.Now:h:mm tt}";
        LastExportRow.IsVisible = true;
    }

    private void OnRevealExport(object? sender, RoutedEventArgs e)
    {
        if (_lastExport is not null) RevealInFinder(_lastExport);
    }

    /// <summary>Opens a Finder window with the file selected.</summary>
    private static void RevealInFinder(string path)
    {
        try
        {
            if (OperatingSystem.IsMacOS()) Process.Start("open", $"-R \"{path}\"");
            else if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe")
                { Arguments = $"/select,\"{path}\"", UseShellExecute = true });
        }
        catch { /* non-fatal: the log already shows where the file went */ }
    }

    /// <summary>Turns Console output into one UI-thread callback per line.</summary>
    private sealed class UiLogWriter : TextWriter
    {
        private readonly Action<string> _emit;
        private readonly TextWriter? _also;
        private readonly StringBuilder _line = new();

        /// <param name="also">Another writer to copy everything to — the original stdout.</param>
        public UiLogWriter(Action<string> emit, TextWriter? also = null)
        {
            _emit = emit;
            _also = also;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            _also?.Write(value);
            if (value == '\n') { _emit(_line.ToString()); _line.Clear(); }
            else if (value != '\r') _line.Append(value);
        }

        public override void Flush()
        {
            _also?.Flush();
            if (_line.Length > 0) { _emit(_line.ToString()); _line.Clear(); }
        }
    }
}
