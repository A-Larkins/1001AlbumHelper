using Avalonia;

namespace _1001AlbumHelper;

internal static class Program
{
    // Default: launch the desktop app. Use `dotnet run -- console` for the classic text menu.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("console") || args.Contains("--console"))
        {
            ConsoleMenu.RunAsync().GetAwaiter().GetResult();
            return;
        }

        // Headless check of the Google Sheets potentials sync (verifies access + seeds the sheet).
        if (args.Contains("synctest"))
        {
            SyncDiagnostic.RunAsync().GetAwaiter().GetResult();
            return;
        }

        // Headless, self-cleaning check of the mobile-safe REST Sheets client.
        if (args.Contains("resttest"))
        {
            RestSheetsClientDiagnostic.RunAsync().GetAwaiter().GetResult();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Used by the Avalonia designer and by Main.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
