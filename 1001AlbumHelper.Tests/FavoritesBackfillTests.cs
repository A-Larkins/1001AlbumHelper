using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// The bookkeeping the favourites backfill decides from. The Apple Music round trip itself can only
/// be exercised on a Mac with the Music app running (that's what <c>favtest</c> is for) — what's
/// worth pinning here is the arithmetic the card and the log report, since a wrong count is the
/// difference between "press to write 412 changes" and "press to write 4".
/// </summary>
public class FavoritesBackfillTests
{
    private static Operations.FavoritesBackfillItem Item(string title, bool favorite, bool playlist) =>
        new(new PlaylistEntry(title, "Artist", "1970"), favorite, playlist);

    private static Operations.FavoritesBackfillPlan Plan(params Operations.FavoritesBackfillItem[] items) =>
        new(items, FavoritedInLibrary: 500, InPlaylist: 250);

    [Fact]
    public void Counts_the_two_halves_independently()
    {
        // The middle album is the case the whole design turns on: already favourited, but missing
        // from the playlist that's meant to duplicate the favourites.
        var plan = Plan(
            Item("Both missing", favorite: true, playlist: true),
            Item("Favourited already", favorite: false, playlist: true),
            Item("In the playlist already", favorite: true, playlist: false),
            Item("Nothing to do", favorite: false, playlist: false));

        Assert.Equal(4, plan.Considered);
        Assert.Equal(2, plan.AlreadyFavorited);
        Assert.Equal(2, plan.ToFavorite);
        Assert.Equal(2, plan.ToAddToPlaylist);
    }

    /// <summary>
    /// The work list is what the backfill walks, and what the card counts down. An album that needs
    /// neither half must not be in it — otherwise a run over a list Apple Music already agrees with
    /// would still cost one library search per album.
    /// </summary>
    [Fact]
    public void Work_skips_the_albums_with_nothing_to_do()
    {
        var plan = Plan(
            Item("Needs a heart", favorite: true, playlist: false),
            Item("Nothing to do", favorite: false, playlist: false),
            Item("Needs the playlist", favorite: false, playlist: true));

        Assert.Equal(new[] { "Needs a heart", "Needs the playlist" },
                     plan.Work.Select(i => i.Album.Title));
    }

    [Fact]
    public void An_agreeing_list_leaves_no_work_at_all()
    {
        var plan = Plan(Item("A", favorite: false, playlist: false),
                        Item("B", favorite: false, playlist: false));

        Assert.Empty(plan.Work);
        Assert.Equal(2, plan.AlreadyFavorited);
    }

    [Theory]
    [InlineData(true, true, "favourite + playlist")]
    [InlineData(true, false, "favourite")]
    [InlineData(false, true, "playlist")]
    [InlineData(false, false, "nothing to do")]
    public void Says_what_it_would_do_to_each_album(bool favorite, bool playlist, string expected)
    {
        Assert.Equal(expected, Item("X", favorite, playlist).Intent);
    }

    /// <summary>A failure carries the reason and nothing else — no half-plan for a caller to act on.</summary>
    [Fact]
    public void A_failed_plan_has_no_work()
    {
        var plan = Operations.FavoritesBackfillPlan.Failed("Google Sheets isn't configured.");

        Assert.Equal("Google Sheets isn't configured.", plan.Error);
        Assert.Equal(0, plan.Considered);
        Assert.Empty(plan.Work);
    }

    /// <summary>
    /// The two lists are matched against Apple Music the loose way, because Apple Music names an
    /// album by whichever edition it stocks. Pinned here because it is what decides whether an
    /// already-favourited album gets counted as needing a heart all over again.
    /// </summary>
    [Fact]
    public void An_album_matches_the_edition_apple_music_stocks()
    {
        var ours = new PlaylistEntry("Tago Mago", "Can", "1971");
        var theirs = new PlaylistEntry("Tago Mago (2011 Remastered)", "Can", "");

        Assert.True(PlaylistStore.SameAlbum(ours, theirs));
    }
}
