using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// The naming gap between the list and Apple Music. The list distinguishes rows with suffixes Music
/// has never heard of — "(self titled)", "(soundtrack)", "(Black Album)" — and asking Music for the
/// literal string returns nothing at all: 44 of the 76 albums the first favorites backfill couldn't
/// find were exactly this shape (2026-08-25). The Mac writer now retries the search with the bare
/// name, and this pins the trimming that produces it, over the titles that actually failed.
/// <para>
/// The writer itself lives in the Desktop head, which this project doesn't reference and which needs
/// a running Music app anyway — so the rule is tested where it lives, in <c>BareTitle</c>.
/// </para>
/// </summary>
public class SearchFallbackTests
{
    [Theory]
    [InlineData("Audioslave (self titled)", "Audioslave")]
    [InlineData("Animals as Leaders (self titled)", "Animals as Leaders")]
    [InlineData("Bachman-Turner Overdrive II (self-titled)", "Bachman-Turner Overdrive II")]
    [InlineData("JT (self titled-ish)", "JT")]
    [InlineData("Tarzan (soundtrack)", "Tarzan")]
    [InlineData("Into the Wild (soundtrack)", "Into the Wild")]
    [InlineData("Metallica (Black Album)", "Metallica")]
    [InlineData("The Beatles (White Album)", "The Beatles")]
    [InlineData("In The Court Of The Crimson King (An Observation By King Crimson)",
                "In The Court Of The Crimson King")]
    [InlineData("Mad Dogs & Englishmen (Live at the Fillmore East)", "Mad Dogs & Englishmen")]
    public void The_list_s_own_furniture_comes_off_the_search_term(string written, string bare)
    {
        Assert.Equal(bare, DiscogsApiClient.BareTitle(written));
    }

    /// <summary>
    /// A title that is wholly a parenthetical has nothing to trim to, so it's handed back as it is —
    /// otherwise Lynyrd Skynyrd's debut would be searched for as the empty string.
    /// </summary>
    [Fact]
    public void A_title_that_is_all_parenthetical_is_left_alone()
    {
        const string written = "(Pronounced 'Lĕh-'nérd 'Skin-'nérd)";
        Assert.Equal(written, DiscogsApiClient.BareTitle(written));
    }

    /// <summary>
    /// Music's own copy wears a parenthetical too, so the fallback compares bare against bare. This
    /// is the pairing that matters: our name and Music's reduce to the same thing.
    /// </summary>
    [Theory]
    [InlineData("Led Zeppelin (self titled)", "Led Zeppelin (Remastered)")]
    [InlineData("Led Zeppelin (self titled)", "Led Zeppelin (Deluxe Edition)")]
    [InlineData("The Clash (self titled)", "The Clash (2013 Remastered)")]
    [InlineData("Joan Baez (self titled)", "Joan Baez (Bonus Track Version)")]
    [InlineData("Tarzan (soundtrack)", "Tarzan (Original Motion Picture Soundtrack)")]
    public void Ours_and_music_s_reduce_to_the_same_album(string ours, string musics)
    {
        Assert.True(NumberedList.Matches(DiscogsApiClient.BareTitle(ours),
                                         DiscogsApiClient.BareTitle(musics)));
    }

    /// <summary>
    /// And the reason the fallback demands equality rather than the usual prefix rule: a search for
    /// "Led Zeppelin" returns the whole run of them, and a prefix match would let the tie-break hand
    /// back II or IV for the self-titled debut.
    /// </summary>
    [Theory]
    [InlineData("Led Zeppelin II (Remastered)")]
    [InlineData("Led Zeppelin III (Deluxe Edition)")]
    [InlineData("Led Zeppelin IV (Remastered)")]
    public void A_numbered_sequel_is_not_the_self_titled_debut(string sequel)
    {
        string ours = DiscogsApiClient.BareTitle("Led Zeppelin (self titled)");

        Assert.False(NumberedList.Matches(DiscogsApiClient.BareTitle(sequel), ours));
        // The old prefix rule is exactly what would have accepted it.
        Assert.True(DiscogsApiClient.TitlesLineUp(DiscogsApiClient.BareTitle(sequel), ours));
    }

    /// <summary>
    /// The other half of the same gap, and the one that stopped the backfill converging: the list
    /// and Apple Music disagree about who made a record, so an album that had just been favorited
    /// read as still missing on the next run — and each run pushed it into the playlist again, which
    /// Apple Music has no way to undo. Detection allows the credits to overlap rather than match,
    /// the same leniency the add path already had. These are the real pairs it has to bridge.
    /// </summary>
    [Theory]
    [InlineData("Ozzy Osborne", "Ozzy Osbourne")]
    [InlineData("Simon And Garfunkel", "Simon & Garfunkel")]
    [InlineData("Neil Young With Crazy Horse", "Neil Young & Crazy Horse")]
    [InlineData("Derek And The Dominos", "Derek & The Dominos")]
    [InlineData("The Flying Burrito Bros", "The Flying Burrito Brothers")]
    [InlineData("Dinosaur", "Dinosaur Jr.")]
    [InlineData("The Wailers", "Bob Marley & The Wailers")]
    [InlineData("James Taylor (2)", "James Taylor")]
    [InlineData("Chicago Transit Authority", "Chicago")]
    [InlineData("Tools and the Maytals", "Toots and the Maytals")]
    public void The_same_act_under_two_credits_is_recognized(string ours, string theirs)
    {
        Assert.True(DiscogsApiClient.ArtistsOverlap(ours, theirs));
    }

    /// <summary>
    /// Overlap is not a license to match anyone: it wants a substantial word in common, so two
    /// unrelated acts stay apart and an album isn't counted as held when it isn't.
    /// </summary>
    [Theory]
    [InlineData("Ozzy Osbourne", "Black Sabbath")]
    [InlineData("The Beatles", "The Rolling Stones")]
    [InlineData("Genesis", "Phil Collins")]
    public void Two_different_acts_stay_apart(string a, string b)
    {
        Assert.False(DiscogsApiClient.ArtistsOverlap(a, b));
    }

    /// <summary>
    /// An album with no furniture yields nothing new, so the writer makes one search rather than
    /// two — the retry costs a Music library search, which is the slow thing in a backfill.
    /// </summary>
    [Theory]
    [InlineData("Kind Of Blue")]
    [InlineData("Exile On Main St.")]
    [InlineData("Live/Dead")]
    public void An_ordinary_title_has_nothing_to_fall_back_to(string title)
    {
        Assert.Equal(title, DiscogsApiClient.BareTitle(title));
    }
}
