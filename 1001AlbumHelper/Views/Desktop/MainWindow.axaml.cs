using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace _1001AlbumHelper;

public partial class MainWindow : Window
{
    private static readonly IBrush IdleDot = new SolidColorBrush(Color.Parse("#7c6d60"));
    private static readonly IBrush RunningDot = new SolidColorBrush(Color.Parse("#e6a54b"));
    private const string LogPlaceholder = "Ready. Pick an action above — output streams here live.";

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

        Opened += async (_, _) => await RefreshSyncStatusAsync();

        InsightsSection.IsVisible = AnalyticsWindowHost.Factory is not null;
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
            return;
        }

        if (status.NeedsSync)
        {
            SyncDescText.Text = status.Summary();
            SyncBadgeText.Text = "NEEDED";
            SyncBadge.IsVisible = true;
        }
        else
        {
            SyncDescText.Text = "Everything's in sync — nothing to reconcile.";
            SyncBadge.IsVisible = false;
        }
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

            // Reveal the finished PDF rather than making them hunt for it.
            if (exportedPdf is not null) RevealInFinder(exportedPdf);
        });
    }

    private void SetRunning(bool on)
    {
        StatusText.Text = on ? "Running…" : "Idle";
        StatusDot.Fill = on ? RunningDot : IdleDot;
        CheckButton.IsEnabled = !on;
        RenewTrialButton.IsEnabled = !on;
        RateNextButton.IsEnabled = !on;
        BackfillButton.IsEnabled = !on;
        AddAlbumButton.IsEnabled = !on;
        BrowseButton.IsEnabled = !on;
        CandidatesButton.IsEnabled = !on;
        SyncButton.IsEnabled = !on;
        ExportButton.IsEnabled = !on;
    }

    /// <summary>
    /// Renews the iPhone's 7-day free-account signing profile and redeploys, by shelling out to
    /// deploy-to-device.sh — the same script used from the terminal (see PROJECT.md §4 and §7).
    /// </summary>
    private async void OnRenewTrial(object? sender, RoutedEventArgs e)
    {
        if (!_gate.Wait(0)) return;

        SetRunning(true);
        LogBox.Text = "";
        _startedAt = DateTime.Now;
        _timer.Start();

        var sw = Stopwatch.StartNew();
        bool ok = false;
        string? err = null;

        await Task.Run(async () =>
        {
            try
            {
                var scriptPath = Path.Combine(FindRepoRoot(), "1001AlbumHelper.iOS", "deploy-to-device.sh");

                var psi = new ProcessStartInfo("/bin/bash", $"\"{scriptPath}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };

                using var proc = Process.Start(psi)!;
                proc.OutputDataReceived += (_, args) =>
                {
                    if (args.Data is not null) Dispatcher.UIThread.Post(() => AppendLine(args.Data));
                };
                proc.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data is not null) Dispatcher.UIThread.Post(() => AppendLine(args.Data));
                };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                await proc.WaitForExitAsync();

                if (proc.ExitCode != 0) throw new InvalidOperationException($"exited with code {proc.ExitCode}");
                ok = true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
            }
        });

        sw.Stop();

        Dispatcher.UIThread.Post(() =>
        {
            _timer.Stop();
            AppendLine("");
            AppendLine(ok
                ? $"✓ Finished in {sw.Elapsed.TotalSeconds:0.0}s."
                : $"✗ Failed{(err != null ? ": " + err : ".")}");
            SetRunning(false);
            _gate.Release();
        });
    }

    private void AppendLine(string line)
    {
        LogBox.Text = LogBox.Text?.Length > 0 ? LogBox.Text + "\n" + line : line;
        LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
    }

    private void OnClearLog(object? sender, RoutedEventArgs e) => LogBox.Text = LogPlaceholder;

    // ---------- Rating & adding ----------
    private void OnRateNext(object? sender, RoutedEventArgs e) => OpenRater(RatingMode.NextUp);
    private void OnBackfill(object? sender, RoutedEventArgs e) => OpenRater(RatingMode.Backfill);

    private void OpenRater(RatingMode mode)
    {
        // Don't open mid-run: both talk to the same sheet.
        if (!RateNextButton.IsEnabled) return;
        new RatingWindow(mode).ShowDialog(this);
    }

    private void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (!BrowseButton.IsEnabled) return;
        new ListViewerWindow().ShowDialog(this);
    }

    private void OnPlaylists(object? sender, RoutedEventArgs e)
    {
        if (!PlaylistsButton.IsEnabled) return;
        new PlaylistWindow().ShowDialog(this);
    }

    private async void OnAddAlbum(object? sender, RoutedEventArgs e)
    {
        if (!AddAlbumButton.IsEnabled) return;
        await ShowLoggingDialogAsync(new AddAlbumWindow());
    }

    private async void OnCandidates(object? sender, RoutedEventArgs e)
    {
        if (!CandidatesButton.IsEnabled) return;
        await ShowLoggingDialogAsync(new CandidatesWindow());
    }

    private void OnAnalytics(object? sender, RoutedEventArgs e)
    {
        var window = AnalyticsWindowHost.Factory?.Invoke();
        window?.ShowDialog(this);
    }

    /// <summary>
    /// Shows a dialog that writes to the sheet. Those writes go through Operations, whose progress
    /// goes to Console — capture it into the log the same way a run does, so the main window still
    /// shows what happened once the dialog is closed.
    /// </summary>
    private async Task ShowLoggingDialogAsync(Window dialog)
    {
        var writer = new UiLogWriter(line => Dispatcher.UIThread.Post(() => AppendLine(line)));
        var previousOut = Console.Out;
        Console.SetOut(writer);
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            Console.SetOut(previousOut);
            writer.Flush();
        }
    }

    /// <summary>
    /// Walks up from the running app's directory to find the repo root, identified by a sibling
    /// "1001AlbumHelper.iOS" directory (a plain *.sln check is ambiguous — there's one both at the
    /// true repo root and one level down, inside the shared-library project folder). Falls back to the
    /// known checkout path since the published app (installed to /Applications) is disconnected from
    /// the source tree it was built from.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "1001AlbumHelper.iOS"))) return dir.FullName;
            dir = dir.Parent;
        }

        const string fallback = "/Users/alarks/code/C#/1001AlbumHelper";
        if (Directory.Exists(fallback)) return fallback;

        throw new InvalidOperationException("Couldn't locate the repo root from " + AppContext.BaseDirectory);
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
        private readonly StringBuilder _line = new();

        public UiLogWriter(Action<string> emit) => _emit = emit;

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n') { _emit(_line.ToString()); _line.Clear(); }
            else if (value != '\r') _line.Append(value);
        }

        public override void Flush()
        {
            if (_line.Length > 0) { _emit(_line.ToString()); _line.Clear(); }
        }
    }
}
