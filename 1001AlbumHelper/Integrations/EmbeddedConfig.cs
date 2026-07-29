using Microsoft.Extensions.Configuration;

namespace _1001AlbumHelper;

/// <summary>
/// Reads config/secret files the same way everywhere: the copy next to the executable, then the
/// live copy in the data folder (so editing it takes effect without a rebuild), then — for mobile,
/// which has neither a data folder nor a writable bundle — the copy baked into the assembly as an
/// embedded resource (see the shared project's .csproj).
/// </summary>
public static class EmbeddedConfig
{
    /// <summary>Layered JSON config (e.g. "appsettings.json").</summary>
    public static IConfigurationRoot Load(string fileName)
    {
        var builder = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, fileName), optional: true)
            .AddJsonFile(Path.Combine(Operations.ProjectDir, fileName), optional: true);
        if (ReadEmbeddedStream(fileName) is { } stream) builder.AddJsonStream(stream);
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
