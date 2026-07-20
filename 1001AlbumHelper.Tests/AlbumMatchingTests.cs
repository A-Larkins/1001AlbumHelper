using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// The year lookup drops a search filter at a time until something comes back, so these two rules
/// are the only thing standing between a hard-to-find album and a confidently wrong year.
/// </summary>
public class AlbumMatchingTests
{
    [Theory]
    [InlineData("Vs.", "Vs.")]
    [InlineData("SONGS FOR THE DEAF", "Songs for the Deaf")]                  // case
    [InlineData("...And Out Come The Wolves", "...and out Come the Wolves")]  // punctuation
    [InlineData("Hüsker Dü", "Husker Du")]                                    // accents
    public void The_same_album_lines_up_however_it_is_written(string a, string b)
    {
        Assert.True(DiscogsApiClient.TitlesLineUp(a, b));
    }

    [Theory]
    [InlineData("This One's For You", "This One's for You Too")]
    [InlineData("What You See Is What You Get", "What You See Is What You Get (Deluxe Edition)")]
    [InlineData("Kirk Franklin and the Family", "Kirk Franklin and the Family (Live)")]
    public void An_edition_lines_up_with_the_album_it_extends(string album, string edition)
    {
        Assert.True(DiscogsApiClient.TitlesLineUp(album, edition));
    }

    [Theory]
    [InlineData("Four", "Fourty Licks")]        // a prefix mid-word is not a prefix
    [InlineData("Core", "Hardcore")]
    [InlineData("Anthem", "Phantom Anthem")]    // extended at the front, not the back
    public void A_different_album_does_not_line_up(string a, string b)
    {
        Assert.False(DiscogsApiClient.TitlesLineUp(a, b));
    }

    [Fact]
    public void A_title_alone_cannot_tell_an_edition_from_a_namesake()
    {
        // The prefix rule that lets a deluxe match its album also lets Prince's record match
        // Stone Temple Pilots', because the two cases are indistinguishable on title. This is
        // the reason the searches that aren't bound to an artist check one as well.
        Assert.True(DiscogsApiClient.TitlesLineUp("Purple", "Purple Rain"));
        Assert.False(DiscogsApiClient.ArtistsOverlap("Prince", "Stone Temple Pilots"));
    }

    [Fact]
    public void An_empty_title_never_lines_up()
    {
        Assert.False(DiscogsApiClient.TitlesLineUp("", "Vs."));
        Assert.False(DiscogsApiClient.TitlesLineUp("Vs.", "   "));
    }

    [Theory]
    // How Discogs actually bills these, against what the playlist calls them.
    [InlineData("The Costello Show Featuring The Attractions And Confederates", "Elvis Costello")]
    [InlineData("Neil Young With Crazy Horse", "Neil Young & Crazy Horse")]
    [InlineData("Hootie & The Blowfish", "Hootie and the Blowfish")]
    public void A_differently_billed_artist_is_still_recognised(string discogs, string ours)
    {
        Assert.True(DiscogsApiClient.ArtistsOverlap(discogs, ours));
    }

    [Theory]
    [InlineData("The Band", "The Beatles")]
    [InlineData("Owen", "Owl City")]
    [InlineData("Prince", "Stone Temple Pilots")]
    public void Unrelated_acts_do_not_overlap(string a, string b)
    {
        Assert.False(DiscogsApiClient.ArtistsOverlap(a, b));
    }

    [Fact]
    public void Overlap_is_plausibility_rather_than_identity()
    {
        // Two acts sharing a substantial word are treated as possibly the same, which "Pearl Jam"
        // and "Pearl Harbour" are not. That is tolerable only because overlap is never the whole
        // test: the album's title has to line up as well before a year is taken from it.
        Assert.True(DiscogsApiClient.ArtistsOverlap("Pearl Jam", "Pearl Harbour"));
        Assert.False(DiscogsApiClient.TitlesLineUp("Vs.", "Pearl Harbour Soundtrack"));
    }

    [Fact]
    public void Short_words_alone_cannot_relate_two_acts()
    {
        // "and"/"the" are shared by half the artists alive; only substantial words count.
        Assert.False(DiscogsApiClient.ArtistsOverlap("The Kinks and Co", "The Doors and Co"));
    }

    [Fact]
    public void An_artist_with_no_substantial_word_is_never_matched_loosely()
    {
        // "R.E.M." leaves nothing but single letters to compare, so the loose searches decline it
        // rather than guess. Their albums are found by the artist-bound searches that run first.
        Assert.False(DiscogsApiClient.ArtistsOverlap("R.E.M.", "R.E.M."));
    }
}
