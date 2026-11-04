using System.Text.Json;
using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// The offline fallback is a JSON copy of the last good read of the lists. It's only worth having if
/// it reads back whole — records with constructor parameters are where serializers tend to slip.
/// </summary>
public class ListsSnapshotTests
{
    [Fact]
    public void The_lists_survive_a_round_trip_through_the_snapshot()
    {
        var lists = new Operations.AllLists(
            new[] { new AlbumEntry(8, "756", "👍", "Tical", "Method Man", "1994", "Hip Hop") },
            new[] { new NumberedList.Row(3, "1", "Illmatic", "Nas", "1994") },
            new[] { new NumberedList.Row(4, "1", "Trapt", "Trapt", "2002") },
            CachedAt: new DateTime(2026, 11, 3, 17, 0, 0));

        var back = JsonSerializer.Deserialize<Operations.AllLists>(JsonSerializer.Serialize(lists))!;

        Assert.Equal(lists.Master.Single(), back.Master.Single());
        Assert.Equal(lists.MustHear.Single(), back.MustHear.Single());
        Assert.Equal(lists.Replacements.Single(), back.Replacements.Single());
        Assert.Equal(lists.CachedAt, back.CachedAt);
        Assert.Null(back.Error);
    }
}
