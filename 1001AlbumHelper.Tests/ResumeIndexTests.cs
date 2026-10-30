using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// Where the browse list opens on the 1001: the album after the furthest one rated, so the user
/// lands in the 750s rather than at #1 — and isn't dragged back by an album skipped long ago.
/// </summary>
public class ResumeIndexTests
{
    private static ViewRow[] Rows(params string[] ratings) =>
        ratings.Select((r, i) => new ViewRow($"{i + 1}", r, "T", "A", "1970")).ToArray();

    [Fact]
    public void Lands_on_the_row_after_the_furthest_mark()
    {
        Assert.Equal(3, ViewRow.ResumeIndex(Rows("👍", "⭐", "❌", "", "")));
    }

    [Fact]
    public void A_gap_further_back_is_not_where_the_user_is()
    {
        Assert.Equal(4, ViewRow.ResumeIndex(Rows("👍", "", "👎", "✓", "", "")));
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
