using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// Where the browse list opens on the 1001: the first unrated album, the one the Rate page's Next up
/// shows — so the user lands in the 750s rather than at #1, or at #958 because one album far ahead
/// happened to get rated early.
/// </summary>
public class ResumeIndexTests
{
    private static ViewRow[] Rows(params string[] ratings) =>
        ratings.Select((r, i) => new ViewRow($"{i + 1}", r, "T", "A", "1970")).ToArray();

    [Fact]
    public void Lands_on_the_first_unmarked_row()
    {
        Assert.Equal(3, ViewRow.ResumeIndex(Rows("👍", "⭐", "❌", "", "")));
    }

    [Fact]
    public void One_album_rated_far_ahead_does_not_move_it()
    {
        Assert.Equal(2, ViewRow.ResumeIndex(Rows("👍", "✓", "", "", "", "👍", "")));
    }

    [Fact]
    public void Nothing_marked_starts_at_the_top()
    {
        Assert.Equal(0, ViewRow.ResumeIndex(Rows("", "", "")));
    }

    [Fact]
    public void Everything_marked_lands_on_the_last_row()
    {
        Assert.Equal(2, ViewRow.ResumeIndex(Rows("👍", "👍", "⭐")));
    }

    [Fact]
    public void An_empty_list_is_index_zero()
    {
        Assert.Equal(0, ViewRow.ResumeIndex(Array.Empty<ViewRow>()));
    }
}
