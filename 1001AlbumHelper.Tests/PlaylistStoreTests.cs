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
    public void Pulling_down_names_the_albums_it_took_in_not_just_how_many()
    {
        // Playlist 2's pull passes these on to the potentials shortlist, so which albums arrived
        // matters, not merely the count.
        var store = PlaylistStore.Open(_id);
        store.Add("Vs.", "Pearl Jam", "1993");
        store.MarkInAppleMusic(store.Active[0]);

        var sync = store.SyncFromAppleMusic(new[]
        {
            new PlaylistEntry("Vs.", "Pearl Jam", ""),
            new PlaylistEntry("Zuma", "Neil Young", ""),
        });

        var arrived = Assert.Single(sync.NewAlbums);
        Assert.Equal("Zuma", arrived.Title);   // the album already on the list isn't "new"
        Assert.True(arrived.InAppleMusic);
    }

    /// <summary>
    /// The real case: Tical 2000 went in by mistake, was taken off (so it sits on the removal
    /// checklist), and the user swapped in the real Tical by hand in Music. The pull must bring
    /// Tical in as its own album and see that Tical 2000 is gone — not file one under the other
    /// because one title starts with the other.
    /// </summary>
    [Fact]
    public void Pulling_down_never_mistakes_an_album_for_one_whose_title_it_starts()
    {
        var store = PlaylistStore.Open(_id);
        store.Add("Tical 2000: Judgement Day", "Method Man", "");
        store.MarkInAppleMusic(store.Active[0]);
        store.RequestRemoval(store.Active[0]);

        var sync = store.SyncFromAppleMusic(new[]
        {
            new PlaylistEntry("Tical", "Method Man", "") { TrackCount = 13 },
        });

        Assert.Equal("Tical", Assert.Single(store.Active).Title);
        Assert.Empty(store.ToRemove);
        Assert.Equal(1, sync.ClearedFromChecklist);
    }

    [Fact]
    public void Pulling_down_still_sees_a_remaster_as_the_album_it_already_has()
    {
        var store = PlaylistStore.Open(_id);
        store.Add("Haunted Dancehall", "The Sabres Of Paradise", "1994");

        var sync = store.SyncFromAppleMusic(new[]
        {
            new PlaylistEntry("Haunted Dancehall (Remastered)", "The Sabres Of Paradise", "") { TrackCount = 14 },
        });

        Assert.Equal(0, sync.Added);
        Assert.Equal("1994", Assert.Single(store.Active).Year); // the year carried across
    }

    [Fact]
    public void Missing_years_are_filled_and_known_ones_left_alone()
    {
        var store = PlaylistStore.Open(_id);
        store.Add("Tical", "Method Man", "");
        store.Add("Dummy", "Portishead", "1994");
        var asked = new List<string>();

        int filled = store.FillMissingYears(e => { asked.Add(e.Title); return e.Title == "Tical" ? "1994" : null; });

        Assert.Equal(1, filled);
        Assert.Equal(new[] { "Tical" }, asked); // an album with a year is never asked about
        Assert.All(PlaylistStore.Open(_id).Active, e => Assert.Equal("1994", e.Year)); // and it was saved
    }

    [Fact]
    public void Pulling_down_drops_a_sent_album_thats_gone_from_apple_music()
    {
        // Apple Music is the source of truth for what has been there: an album that was sent and
        // isn't any more was deleted in the Music app, so it goes here too.
        var store = PlaylistStore.Open(_id);
        store.Add("Vs.", "Pearl Jam", "1993");
        store.Add("Zuma", "Neil Young", "1975");
        foreach (var e in store.Active.ToList()) store.MarkInAppleMusic(e);

        var sync = store.SyncFromAppleMusic(new[]
        {
            new PlaylistEntry("Zuma", "Neil Young", "") { TrackCount = 9 },
        });

        Assert.Equal(1, sync.Removed);
        Assert.Equal(0, sync.Added);
        Assert.Equal("Zuma", Assert.Single(store.Active).Title);
    }

    [Fact]
    public void Pulling_down_keeps_albums_queued_here_but_not_sent_yet()
    {
        // The reported bug: a batch queued from the shortlist, then a pull, and the batch was gone.
        // An album that was never sent isn't missing from Apple Music — it was never there.
        var store = PlaylistStore.Open(_id);
        store.Add("Zuma", "Neil Young", "1975");
        store.MarkInAppleMusic(store.Active[0]);
        store.Add("Vs.", "Pearl Jam", "1993");

        var sync = store.SyncFromAppleMusic(new[]
        {
            new PlaylistEntry("Harvest", "Neil Young", "") { TrackCount = 10 },
            new PlaylistEntry("Zuma", "Neil Young", "") { TrackCount = 9 },
        });

        Assert.Equal(0, sync.Removed);
        Assert.Equal(1, sync.KeptUnsent);
        Assert.Equal(new[] { "Harvest", "Zuma", "Vs." }, store.Active.Select(e => e.Title));
        var vs = store.Active.Single(e => e.Title == "Vs.");
        Assert.False(vs.InAppleMusic);
        Assert.Equal("1993", vs.Year);
        Assert.Contains(PlaylistStore.Open(_id).Active, e => e.Title == "Vs."); // and it was saved
    }

    [Fact]
    public void Pulling_down_marks_an_unsent_album_sent_when_apple_music_already_has_it()
    {
        // Queued here and added by hand in the Music app too: it's simply there now.
        var store = PlaylistStore.Open(_id);
        store.Add("Tago Mago", "Can", "1971");

        var sync = store.SyncFromAppleMusic(new[]
        {
            new PlaylistEntry("Tago Mago (2011 Remastered)", "Can", "") { TrackCount = 7 },
        });

        Assert.Equal(0, sync.KeptUnsent);
        Assert.Equal(0, sync.Added);
        var album = Assert.Single(store.Active);
        Assert.True(album.InAppleMusic);
        Assert.Equal("Tago Mago", album.Title);
    }

    [Fact]
    public void Pulling_down_into_an_empty_apple_music_playlist_drops_only_what_was_sent()
    {
        var store = PlaylistStore.Open(_id);
        store.Add("Vs.", "Pearl Jam", "1993");
        store.MarkInAppleMusic(store.Active[0]);
        store.Add("Zuma", "Neil Young", "1975");

        var sync = store.SyncFromAppleMusic(Array.Empty<PlaylistEntry>());

        Assert.Equal(1, sync.Removed);
        Assert.Equal("Zuma", Assert.Single(store.Active).Title);
    }

    [Fact]
    public void Contains_is_loose_about_edition_names_and_ignores_the_checklist()
    {
        var store = PlaylistStore.Open(_id);
        store.SyncFromAppleMusic(new[]
        {
            new PlaylistEntry("Cassini (5th Anniversary Remaster)", "Thank You Scientist", "") { TrackCount = 9 },
            new PlaylistEntry("Vs.", "Pearl Jam", "") { TrackCount = 12 },
        });
        store.RequestRemoval(store.Active.Single(e => e.Title == "Vs."));

        Assert.True(store.Contains("Cassini", "Thank You Scientist"));
        Assert.False(store.Add("Cassini", "Thank You Scientist", "2016")); // no second row
        Assert.False(store.Contains("Vs.", "Pearl Jam")); // on the checklist, not the working list
        Assert.False(store.Contains("Tical", "Method Man"));
    }

    [Fact]
    public void Adding_an_album_waiting_on_the_checklist_puts_it_back()
    {
        var store = PlaylistStore.Open(_id);
        store.Add("Vs.", "Pearl Jam", "1993");
        store.MarkInAppleMusic(store.Active[0]);
        store.RequestRemoval(store.Active[0]);

        Assert.True(store.Add("Vs.", "Pearl Jam", "1993"));

        var album = Assert.Single(store.Active);
        Assert.True(album.InAppleMusic); // still in Apple Music, so nothing to send
        Assert.Empty(store.ToRemove);
    }

    [Fact]
    public void Every_screen_shares_one_list_and_hears_about_changes()
    {
        // Pages each holding their own copy is what left ＋ P2 refusing to add after a pull.
        var a = PlaylistStore.Shared(_id);
        var b = PlaylistStore.Shared(_id);
        Assert.Same(a, b);

        int changes = 0;
        b.Changed += () => changes++;
        a.Add("Vs.", "Pearl Jam", "1993");
        a.SyncFromAppleMusic(Array.Empty<PlaylistEntry>());

        Assert.Equal(2, changes);
        Assert.True(b.Contains("Vs.", "Pearl Jam"));
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
        store.MarkInAppleMusic(store.Active[0]); // sent, then deleted in the Music app
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
