using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// Sorting the browse lists. The judgement call worth pinning down is the "#" column: it's the way
/// back out of a sort you no longer want, so it has to reproduce the list's own order exactly —
/// which a string sort of "1", "2", "10" does not.
/// </summary>
public class ViewRowSortTests
{
    private static ViewRow Row(string number, string title = "T", string year = "1970") =>
        new(number, "", title, "Artist", year);

    [Fact]
    public void Number_sorts_numerically_not_as_text()
    {
        var rows = new[] { Row("10"), Row("2"), Row("1"), Row("21") };

        var sorted = ViewRowSort.Sort(rows, ViewRowSortColumn.Number, descending: false);

        Assert.Equal(new[] { "1", "2", "10", "21" }, sorted.Select(r => r.Number));
    }

    [Fact]
    public void Number_reverses_on_the_second_tap()
    {
        var rows = new[] { Row("2"), Row("10"), Row("1") };

        var sorted = ViewRowSort.Sort(rows, ViewRowSortColumn.Number, descending: true);

        Assert.Equal(new[] { "10", "2", "1" }, sorted.Select(r => r.Number));
    }

    /// <summary>
    /// A row with no usable number trails whichever way the column runs, the same way a blank cell
    /// does everywhere else — never in the middle, and never at the top of a descending sort.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rows_without_a_number_always_trail(bool descending)
    {
        var rows = new[] { Row(""), Row("3"), Row("—"), Row("1") };

        var sorted = ViewRowSort.Sort(rows, ViewRowSortColumn.Number, descending);

        Assert.Equal(new[] { "", "—" }, sorted.TakeLast(2).Select(r => r.Number).Order());
    }

    /// <summary>
    /// Sorting by "#" over an untouched list is a no-op — which is the whole point of the button:
    /// it puts the list back exactly as the sheet holds it.
    /// </summary>
    [Fact]
    public void Number_restores_the_order_another_sort_scrambled()
    {
        var original = new[] { Row("1", "Zebra"), Row("2", "Apple"), Row("3", "Mango") };

        var byTitle = ViewRowSort.Sort(original, ViewRowSortColumn.Title, descending: false);
        var back = ViewRowSort.Sort(byTitle, ViewRowSortColumn.Number, descending: false);

        Assert.Equal(original.Select(r => r.Title), back.Select(r => r.Title));
    }
}
