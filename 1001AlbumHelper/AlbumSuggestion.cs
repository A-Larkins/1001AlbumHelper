namespace _1001AlbumHelper;

/// <summary>
/// One album returned by a Discogs lookup, ready to drop into the add-album form.
/// <para>
/// <paramref name="Have"/> is how many Discogs users own a copy. It's kept because it's the
/// only reliable way to tell the canonical release of an album from the long tail of reissues
/// and obscure same-name records — see <see cref="DiscogsApiClient.SearchAlbumsAsync"/>.
/// </para>
/// </summary>
public sealed record AlbumSuggestion(string Title, string Artist, string Year, int Have)
{
    /// <summary>"Blue Train — John Coltrane (1958)", for the dropdown and the collapsed box.</summary>
    public string Display => string.IsNullOrEmpty(Year)
        ? $"{Title} — {Artist}"
        : $"{Title} — {Artist} ({Year})";

    public override string ToString() => Display;
}
