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
    public void Pulling_down_takes_in_albums_apple_music_has_that_the_list_doesnt()
    {
        var store = PlaylistStore.Open(_id);

        var sync = store.SyncFromAppleMusic(new[]
        {
            new PlaylistEntry("Zuma", "Neil Young", "") { TrackCount = 9 },
        });

        Assert.Equal(1, sync.Added);
        var album = Assert.Single(store.Active);
        Assert.True(album.InAppleMusic);
        Assert.Equal(9, album.TrackCount);
    }

    [Fact]
    public void Pulling_down_drops_albums_apple_music_doesnt_have()
    {
        // The point of a pull-down: Apple Music is the source of truth, so a local album it doesn't
        // have is gone afterwards — including one queued here but never pushed.
        var store = PlaylistStore.Open(_id);
        store.Add("Vs.", "Pearl Jam", "1993");
        store.Add("Zuma", "Neil Young", "1975");

        var sync = store.SyncFromAppleMusic(new[]
        {
            new PlaylistEntry("Zuma", "Neil Young", "") { TrackCount = 9 },
        });

        Assert.Equal(1, sync.Removed);
        Assert.Equal(0, sync.Added);
        Assert.Equal("Zuma", Assert.Single(store.Active).Title);
    }

    [Fact]
    public void Pulling_down_into_an_empty_apple_music_playlist_empties_the_list()
    {
        var store = PlaylistStore.Open(_id);
        store.Add("Vs.", "Pearl Jam", "1993");

        var sync = store.SyncFromAppleMusic(Array.Empty<PlaylistEntry>());

        Assert.Equal(1, sync.Removed);
        Assert.Empty(store.Active);
    }

    [Fact]
    public void Pulling_down_keeps_the_year_we_already_knew()
    {
        // Apple Music's read-back carries no year, but our lists do — losing it on every sync
        // would strip the year off every album the user had queued from the 1001.
        var store = PlaylistStore.Open(_id);
        store.Add("Vs.", "Pearl Jam", "1993");

        store.SyncFromAppleMusic(new[] { new PlaylistEntry("Vs.", "Pearl Jam", "") { TrackCount = 12 } });

        var album = Assert.Single(store.Active);
        Assert.Equal("1993", album.Year);
        Assert.Equal(12, album.TrackCount);
        Assert.True(album.InAppleMusic);
    }

    [Fact]
    public void Pulling_down_prefers_our_album_name_over_the_catalogs_reissue_name()
    {
        var store = PlaylistStore.Open(_id);
        store.Add("Tago Mago", "Can", "1971");

        store.SyncFromAppleMusic(new[]
        {
            new PlaylistEntry("Tago Mago (2011 Remastered)", "Can", "") { TrackCount = 7 },
        });

        // Same album by our matching rules, so the tidier name we already hold is the one kept.
        Assert.Equal("Tago Mago", Assert.Single(store.Active).Title);
    }

    [Fact]
    public void Pulling_down_does_not_resurrect_an_album_awaiting_manual_deletion()
    {
        // The checklist exists precisely because Apple Music still has these. If a pull-down put
        // them back on the working list, the user's removal would be undone every time they synced.
        var store = PlaylistStore.Open(_id);
        store.Add("Vs.", "Pearl Jam", "1993");
        store.MarkInAppleMusic(store.Active[0]);
        store.RequestRemoval(store.Active[0]);
        Assert.Single(store.ToRemove);

        var sync = store.SyncFromAppleMusic(new[]
        {
            new PlaylistEntry("Vs.", "Pearl Jam", "") { TrackCount = 12 },
        });

        Assert.Empty(store.Active);
        Assert.Single(store.ToRemove);
        Assert.Equal(0, sync.Added);
        Assert.Equal(0, sync.ClearedFromChecklist);
    }

    [Fact]
    public void Pulling_down_ticks_off_a_checklist_album_that_has_gone_from_apple_music()
    {
        // Gone from Apple Music means the user has done the manual deletion — so stop nagging.
        var store = PlaylistStore.Open(_id);
        store.Add("Vs.", "Pearl Jam", "1993");
        store.MarkInAppleMusic(store.Active[0]);
        store.RequestRemoval(store.Active[0]);

        var sync = store.SyncFromAppleMusic(Array.Empty<PlaylistEntry>());

        Assert.Equal(1, sync.ClearedFromChecklist);
        Assert.Empty(store.ToRemove);
        Assert.Empty(store.Active);
    }

    [Fact]
    public void Pulling_down_survives_a_reopen()
    {
        var store = PlaylistStore.Open(_id);
        store.Add("Vs.", "Pearl Jam", "1993");
        store.SyncFromAppleMusic(new[] { new PlaylistEntry("Zuma", "Neil Young", "") { TrackCount = 9 } });

        var reopened = PlaylistStore.Open(_id);

        Assert.Equal("Zuma", Assert.Single(reopened.Active).Title);
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
