using System.Diagnostics;

namespace _1001AlbumHelper;

/// <summary>
/// Showing a finished export to the person who made it. A PDF opens in a new tab of the default
/// web browser — Safari and Chrome both render PDFs in place, with zoom, search and print — which
/// is quicker to read than a Finder window with the file merely selected (what export used to do).
/// <para>
/// The default browser is asked of macOS each time (whatever opens https links), so it follows a
/// change of browser. If none can be found, the file opens in its default app instead (Preview).
/// Paths go to <c>open</c> as separate arguments, never spliced into a command line, so a quote or
/// space in a file name can't break it.
/// </para>
/// </summary>
public static class ExportedFile
{
    /// <summary>Opens the file in a browser tab, else in its default app. Never throws.</summary>
    public static async Task OpenAsync(string path)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var browser = await DefaultBrowserAsync();
                if (browser is not null && await RunAsync("open", "-a", browser, path) == 0) return;
                await RunAsync("open", path);
            }
            else
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch { /* non-fatal: the caller has already said where the file went */ }
    }

    /// <summary>Shows the file selected in Finder (or Explorer). Never throws.</summary>
    public static void Reveal(string path)
    {
        try
        {
            if (OperatingSystem.IsMacOS()) Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-R", path } });
            else if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { $"/select,{path}" } });
        }
        catch { /* non-fatal */ }
    }

    /// <summary>
    /// The app macOS would open an https link with — the default browser — as a path to its
    /// bundle, or null if that can't be found out. Asked through AppKit's NSWorkspace via JXA,
    /// since nothing simpler on the command line answers it.
    /// </summary>
    private static async Task<string?> DefaultBrowserAsync()
    {
        const string script =
            "ObjC.import('AppKit');" +
            "var u = $.NSWorkspace.sharedWorkspace.URLForApplicationToOpenURL($.NSURL.URLWithString('https://example.com'));" +
            "u.isNil() ? '' : u.path.js";
        var psi = new ProcessStartInfo("osascript")
        {
            ArgumentList = { "-l", "JavaScript", "-e", script },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return null;
        string output = (await p.StandardOutput.ReadToEndAsync()).Trim();
        await p.WaitForExitAsync();
        return p.ExitCode == 0 && output.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(output)
            ? output
            : null;
    }

    private static async Task<int> RunAsync(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        if (p is null) return -1;
        await p.WaitForExitAsync();
        return p.ExitCode;
    }
}
