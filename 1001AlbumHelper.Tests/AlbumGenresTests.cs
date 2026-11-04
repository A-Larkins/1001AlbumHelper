using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// The pure halves of the genre finder: putting Apple Music's genre names into the words the lists
/// already use, and borrowing a genre from the artist's other albums as a last resort.
/// </summary>
public class AlbumGenresTests
{
    [Theory]
    [InlineData("Hip-Hop/Rap", "Hip Hop")]
    [InlineData("R&B/Soul", "Funk / Soul")]
    [InlineData("Alternative", "Alternative Rock")]
    [InlineData("Country", "Country")]
    [InlineData("Rock", "Rock")]
    public void Apple_Music_genres_use_the_lists_own_words(string apple, string expected)
    {
        Assert.Equal(expected, AlbumGenres.FromAppleMusic(apple));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Music")]
    public void A_genre_that_says_nothing_is_no_genre(string? apple)
    {
        Assert.Null(AlbumGenres.FromAppleMusic(apple));
    }

    [Fact]
    public void The_artists_usual_genre_is_borrowed_by_its_primary_tag()
    {
        var known = new[]
        {
            ("Townes Van Zandt", "Folk, Country"),
            ("Townes Van Zandt", "Folk"),
            ("Townes Van Zandt", "Country, Folk"),
            ("Tony Rice", "Bluegrass"),
        };

        Assert.Equal("Folk", AlbumGenres.FromArtist(known, "Townes Van Zandt"));
    }

    [Fact]
    public void Nothing_is_borrowed_from_a_different_artist()
    {
        var known = new[] { ("Neil Young", "Folk Rock") };

        Assert.Null(AlbumGenres.FromArtist(known, "Neil Young & Crazy Horse"));
    }
}
