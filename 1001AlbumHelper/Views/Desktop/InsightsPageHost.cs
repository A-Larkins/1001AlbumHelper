using Avalonia.Controls;

namespace _1001AlbumHelper;

/// <summary>
/// Lets <see cref="MainWindow"/> show the Insights page without the shared library taking a
/// direct dependency on it.
/// <para>
/// The page lives in the Desktop head because it used to need a charting package (LiveCharts) the
/// iOS head mustn't link. Since 2026-11-04 it draws its charts with plain Avalonia shapes and no
/// package at all, so it could move into the shared library and this indirection go. Until then
/// the Desktop head sets <see cref="Factory"/> at startup; on any other head it stays null and the
/// Insights section is simply absent.
/// </para>
/// </summary>
public static class InsightsPageHost
{
    public static Func<Control>? Factory { get; set; }
}
