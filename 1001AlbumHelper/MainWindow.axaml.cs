using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace _1001AlbumHelper;

public partial class MainWindow : Window
{
    private static readonly IBrush IdleDot = new SolidColorBrush(Color.Parse("#7c6d60"));
    private static readonly IBrush RunningDot = new SolidColorBrush(Color.Parse("#e6a54b"));
    private const string LogPlaceholder = "Pick an action to get started. Output will stream here live.";

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

        RefreshFiles();
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

        await Task.Run(async () =>
        {
            var previousOut = Console.Out;
            Console.SetOut(writer);
            try
            {
                switch (action)
                {
                    case "download": await Operations.DownloadGoogleSheetsAsync(); break;
                    case "starred": Operations.CreateStarredAlbumsList(); break;
                    case "renumber": Operations.RenumberReplacementAlbums(); break;
                    case "fetch": await Operations.FetchFreshListAsync(); break;
                    case "merge": Operations.MergeRatingsWithDiscogsList(); break;
                    case "sync-both": await Operations.SyncBothToSheetsAsync(); break;
                    case "sync-starred": await Operations.SyncStarredToSheetAsync(); break;
                    case "sync-replacements": await Operations.SyncReplacementsToSheetAsync(); break;
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
            RefreshFiles();
            _gate.Release();
        });
    }

    private void SetRunning(bool on)
    {
        StatusText.Text = on ? "Running…" : "Idle";
        StatusDot.Fill = on ? RunningDot : IdleDot;
        foreach (var btn in ActionsPanel.Children.OfType<Button>())
            btn.IsEnabled = !on;
    }

    private void AppendLine(string line)
    {
        LogBox.Text = LogBox.Text?.Length > 0 ? LogBox.Text + "\n" + line : line;
        LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
    }

    private void OnClearLog(object? sender, RoutedEventArgs e) => LogBox.Text = LogPlaceholder;

    // ---------- Files ----------
    private void RefreshFiles()
    {
        try
        {
            var dir = Operations.OutputDir;
            List<string> names = Directory.Exists(dir)
                ? new DirectoryInfo(dir).GetFiles()
                    .Where(f => !f.Name.StartsWith('.'))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => f.Name)
                    .ToList()
                : new List<string>();
            FilesList.ItemsSource = names;
        }
        catch { /* non-fatal */ }
    }

    private string? SelectedFilePath()
    {
        if (FilesList.SelectedItem is not string name || string.IsNullOrEmpty(name)) return null;
        return Path.Combine(Operations.OutputDir, name);
    }

    private void OnOpenFile(object? sender, RoutedEventArgs e) => Reveal(SelectedFilePath(), reveal: false);
    private void OnRevealFile(object? sender, RoutedEventArgs e) => Reveal(SelectedFilePath(), reveal: true);
    private void OnFileDoubleTapped(object? sender, TappedEventArgs e) => Reveal(SelectedFilePath(), reveal: false);
    private void OnOpenOutputFolder(object? sender, RoutedEventArgs e) => OpenPath(Operations.OutputDir);
    private void OnOpenInputFolder(object? sender, RoutedEventArgs e) => OpenPath(Operations.InputDir);

    private static void Reveal(string? path, bool reveal)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            if (OperatingSystem.IsMacOS())
                Process.Start("open", reveal ? $"-R \"{path}\"" : $"\"{path}\"");
            else if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(reveal ? "explorer.exe" : path)
                { Arguments = reveal ? $"/select,\"{path}\"" : "", UseShellExecute = true });
            else
                Process.Start("xdg-open", reveal ? $"\"{Path.GetDirectoryName(path)}\"" : $"\"{path}\"");
        }
        catch { /* non-fatal */ }
    }

    private static void OpenPath(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            if (OperatingSystem.IsMacOS()) Process.Start("open", $"\"{dir}\"");
            else if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            else Process.Start("xdg-open", $"\"{dir}\"");
        }
        catch { /* non-fatal */ }
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
