using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace _1001AlbumHelper;

/// <summary>Where a candidate has got to. Only <see cref="Pending"/> ones are offered.</summary>
public enum CandidateStatus
{
    Pending,
    Added,
    Declined,
}

/// <summary>
/// One album being considered for the replacements list.
/// <para>
/// Mutable and change-notifying rather than a record, because the year is edited in place in the
/// list and the note updates as an add is attempted.
/// </para>
/// </summary>
public sealed class CandidateAlbum : INotifyPropertyChanged
{
    private string _year = "";
    private string _note = "";

    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Genre { get; set; } = "";
    public CandidateStatus Status { get; set; } = CandidateStatus.Pending;

    /// <summary>Blank until looked up on Discogs or typed in — the seed list has no years.</summary>
    public string Year
    {
        get => _year;
        set => Set(ref _year, value ?? "");
    }

    /// <summary>Per-row feedback ("Discogs says 1994…", "already on the 1001 list"). Not persisted.</summary>
    [JsonIgnore]
    public string Note
    {
        get => _note;
        set => Set(ref _note, value ?? "");
    }

    [JsonIgnore]
    public string Display => $"{Title} — {Artist}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(Note)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNote)));
    }

    [JsonIgnore]
    public bool HasNote => _note.Length > 0;
}

/// <summary>
/// The local shortlist of albums that might earn a place on the replacements list, stored as JSON
/// beside the app's other data.
/// <para>
/// Declined and added albums are kept in the file rather than deleted, so a candidate that's been
/// ruled on once never reappears — the file is the record of what's been decided, not just what's
/// left to decide.
/// </para>
/// </summary>
public static class ReplacementCandidates
{
    public const string FileName = "replacement-candidates.json";

    public static string FilePath => Path.Combine(Operations.ProjectDir, FileName);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Everything in the file, in file order. Returns an empty list when there's no file.</summary>
    public static List<CandidateAlbum> Load() => Load(FilePath);

    /// <inheritdoc cref="Load()"/>
    public static List<CandidateAlbum> Load(string path)
    {
        if (!File.Exists(path)) return new List<CandidateAlbum>();

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json)) return new List<CandidateAlbum>();

        var loaded = JsonSerializer.Deserialize<List<CandidateAlbum>>(json, Options);
        return loaded ?? new List<CandidateAlbum>();
    }

    /// <summary>
    /// Writes via a temporary file and a move, so an interrupted save can't leave the shortlist
    /// half-written — this file is the only copy of which albums have been ruled out.
    /// </summary>
    public static void Save(IEnumerable<CandidateAlbum> albums) => Save(FilePath, albums);

    /// <inheritdoc cref="Save(IEnumerable{CandidateAlbum})"/>
    public static void Save(string path, IEnumerable<CandidateAlbum> albums)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(albums.ToList(), Options));
        File.Move(temp, path, overwrite: true);
    }
}
