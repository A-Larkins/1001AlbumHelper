namespace _1001AlbumHelper;

/// <summary>
/// One album row as shown in a list view — the desktop list viewer and the mobile List tab.
/// Rating is blank for the derived lists. Top-level (not nested) so the XAML compiler can bind
/// to it via x:DataType.
/// </summary>
public sealed record ViewRow(string Number, string Rating, string Title, string Artist, string Year)
{
    /// <summary>Everything searchable, flattened once so filtering doesn't redo the work.</summary>
    public string Haystack { get; } =
        $"{Number} {Rating} {Title} {Artist} {Year} " +
        $"{NumberedList.Normalize(Title)} {NumberedList.Normalize(Artist)}";
}
