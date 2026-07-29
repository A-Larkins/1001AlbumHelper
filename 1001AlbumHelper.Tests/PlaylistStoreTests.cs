using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// Apple Music's MediaPlayer API is add-only — it can never tell the app "I removed this" — so the
/// working list is the only place that distinction lives. What matters here is that removing an
/// album that's actually in Apple Music never just vanishes (the user still owes it a manual
/// deletion there); removing one that never made it to Apple Music can vanish outright.
/// </summary>
public class PlaylistStoreTests : IDisposable
{
    // A real (fixed) data folder is used per PlaylistStore.DataDir, so pick a playlist id nobody
    // else uses and delete its file afterward rather than touching the app's own playlists.
    private readonly int _id = Random.Shared.Next(90_000, 100_000);
    private string Path_ => Path.Combine(PlaylistStore.DataDir, $"playlist{_id}.json");

    public void Dispose()
    {
        if (File.Exists(Path_)) File.Delete(Path_);
    }

    [Fact]
    public void A_freshly_added_album_is_active_and_not_yet_in_apple_music()
    {
        var store = PlaylistStore.Open(_id);
        store.Add("Vs.", "Pearl Jam", "1993");

        var album = Assert.Single(store.Active);
        Assert.False(album.InAppleMusic);
        Assert.Empty(store.ToRemove);
    }

    [Fact]
    public void Removing_an_album_never_pushed_to_apple_music_just_deletes_it()
    {
        var store = PlaylistStore.Open(_id);
        store.Add("Vs.", "Pearl Jam", "1993");
        var album = store.Active[0];

        store.RequestRemoval(album);

        Assert.Empty(store.Active);
        Assert.Empty(store.ToRemove);
    }

    [Fact]
    public void Removing_an_album_thats_in_apple_music_moves_it_to_the_checklist_instead_of_deleting_it()
    {
        var store = PlaylistStore.Open(_id);
        store.Add("Vs.", "Pearl Jam", "1993");
        store.MarkInAppleMusic(store.Active[0]);

        store.RequestRemoval(store.Active[0]);

        Assert.Empty(store.Active);
        var pending = Assert.Single(store.ToRemove);
        Assert.Equal("Vs.", pending.Title);
    }

    [Fact]
    public void Confirming_a_manual_deletion_drops_it_off_the_checklist_for_good()
    {
        var store = PlaylistStore.Open(_id);
        store.Add("Vs.", "Pearl Jam", "1993");
        store.MarkInAppleMusic(store.Active[0]);
        store.RequestRemoval(store.Active[0]);

        store.ConfirmRemoved(store.ToRemove[0]);

        Assert.Empty(store.ToRemove);
        Assert.Empty(store.Active);
    }

    [Fact]
    public void Merging_from_apple_music_adds_albums_not_already_on_the_working_list()
    {
        var store = PlaylistStore.Open(_id);

        int added = store.MergeFromAppleMusic(new[]
        {
            new PlaylistEntry("Zuma", "Neil Young", "") { TrackCount = 9 },
        });

        Assert.Equal(1, added);
        var album = Assert.Single(store.Active);
        Assert.True(album.InAppleMusic);
        Assert.Equal(9, album.TrackCount);
    }

    [Fact]
    public void Merging_from_apple_music_marks_an_existing_entry_present_without_duplicating_it()
    {
        var store = PlaylistStore.Open(_id);
        store.Add("Vs.", "Pearl Jam", "1993");

        int added = store.MergeFromAppleMusic(new[]
        {
            new PlaylistEntry("Vs.", "Pearl Jam", "") { TrackCount = 12 },
        });

        Assert.Equal(0, added);
        var album = Assert.Single(store.Active);
        Assert.True(album.InAppleMusic);
        Assert.Equal(12, album.TrackCount);
    }

    [Fact]
    public void A_low_track_count_is_flagged_in_the_display_as_possibly_partial()
    {
        var full = new PlaylistEntry("Vs.", "Pearl Jam", "1993") { TrackCount = 12 };
        var partial = new PlaylistEntry("Vs.", "Pearl Jam", "1993") { TrackCount = 2 };

        Assert.DoesNotContain("partially deleted", full.Display);
        Assert.Contains("partially deleted", partial.Display);
    }

    [Fact]
    public void Everything_survives_a_reload_from_disk()
    {
        var store = PlaylistStore.Open(_id);
        store.Add("Vs.", "Pearl Jam", "1993");
        store.MarkInAppleMusic(store.Active[0]);
        store.Add("Aja", "Steely Dan", "1977");
        store.RequestRemoval(store.Active.Single(a => a.Title == "Aja"));
        // "Aja" never made it to Apple Music, so it should already be gone, not pending.

        var reloaded = PlaylistStore.Open(_id);

        var vs = Assert.Single(reloaded.Active);
        Assert.Equal("Vs.", vs.Title);
        Assert.True(vs.InAppleMusic);
        Assert.Empty(reloaded.ToRemove);
    }
}
