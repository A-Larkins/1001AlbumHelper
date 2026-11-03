using Avalonia.Controls;

namespace _1001AlbumHelper;

/// <summary>
/// Lets <see cref="MainWindow"/> show the Insights page without the shared library taking a
/// direct dependency on it.
/// <para>
/// The Insights page needs a charting package (LiveCharts), which lives only in the Desktop
/// head's <c>.csproj</c> — not the shared library, which the iOS head also builds against. Adding
/// a chart-rendering package to the shared library risks the same trim/AOT crash class that made
/// Google.Apis unusable on iOS (see <c>PROJECT.md</c> §7). The Desktop head sets <see cref="Factory"/>
/// at startup; on any other head it stays null and the Insights section is simply absent.
/// </para>
/// </summary>
public static class InsightsPageHost
{
    public static Func<Control>? Factory { get; set; }
}
