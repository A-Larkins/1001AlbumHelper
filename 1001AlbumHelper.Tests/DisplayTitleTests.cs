using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// Discogs hands back one "Artist - Album" string that has to be taken apart before anything
/// else works. These pin the cases that quietly produce wrong list entries.
/// </summary>
public class DisplayTitleTests
{
    [Fact]
    public void Splits_artist_from_title()
    {
        var (artist, title) = DiscogsApiClient.SplitDisplayTitle("John Coltrane - Blue Train");

        Assert.Equal("John Coltrane", artist);
        Assert.Equal("Blue Train", title);
    }

    [Fact]
    public void Keeps_a_title_that_contains_its_own_dash()
    {
        // Splitting on every " - " would file this album as "Bitches Brew".
        var (artist, title) = DiscogsApiClient.SplitDisplayTitle("Miles Davis - Bitches Brew - Live");

        Assert.Equal("Miles Davis", artist);
        Assert.Equal("Bitches Brew - Live", title);
    }

    [Fact]
    public void Drops_the_numeric_suffix_discogs_uses_for_same_named_artists()
    {
        var (artist, _) = DiscogsApiClient.SplitDisplayTitle("The Cynics (2) - Blue Train Station");

        Assert.Equal("The Cynics", artist);
    }

    [Fact]
    public void Drops_the_asterisk_marking_a_name_variation()
    {
        var (artist, _) = DiscogsApiClient.SplitDisplayTitle("U.K. Subs* - Another Kind Of Blues");

        Assert.Equal("U.K. Subs", artist);
    }

    [Fact]
    public void Keeps_a_bracketed_number_that_is_part_of_the_name()
    {
        // Only a *trailing* "(n)" is Discogs bookkeeping.
        Assert.Equal("Haircut 100", DiscogsApiClient.CleanArtist("Haircut 100"));
        Assert.Equal("Sunset (2) Boulevard", DiscogsApiClient.CleanArtist("Sunset (2) Boulevard"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NoSeparatorHere")]
    public void Yields_no_artist_when_there_is_nothing_to_split(string input)
    {
        var (artist, _) = DiscogsApiClient.SplitDisplayTitle(input);

        Assert.Equal("", artist);
    }
}
