using Microsoft.Extensions.Configuration;

namespace _1001AlbumHelper;

/// <summary>
/// Reads config/secret files the same way everywhere. The copy baked into the assembly at build
/// time (see the shared project's .csproj) is the base — it's what makes the installed app work with
/// nothing on disk — and a copy in the data folder, if there is one, overrides it, so a setting can
/// be changed without a rebuild.
/// </summary>
public static class EmbeddedConfig
{
    /// <summary>Layered JSON config (e.g. "appsettings.json").</summary>
    public static IConfigurationRoot Load(string fileName)
    {
        // Later sources win: built-in first, then the data folder's copy.
        var builder = new ConfigurationBuilder();
        if (ReadEmbeddedStream(fileName) is { } stream) builder.AddJsonStream(stream);
        builder.AddJsonFile(Path.Combine(Operations.ProjectDir, fileName), optional: true);
        return builder.Build();
    }

    /// <summary>A whole file's text (e.g. a service-account key) via the same fallback, or null if it exists nowhere.</summary>
    public static string? ReadFileOrEmbedded(string fileName)
    {
        string path = Path.Combine(Operations.ProjectDir, fileName);
        if (File.Exists(path)) return File.ReadAllText(path);

        using var stream = ReadEmbeddedStream(fileName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Stream? ReadEmbeddedStream(string logicalName) =>
        typeof(EmbeddedConfig).Assembly.GetManifestResourceStream(logicalName);
}
