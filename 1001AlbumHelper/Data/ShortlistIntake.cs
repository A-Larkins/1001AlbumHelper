using System.Linq;

namespace _1001AlbumHelper;

/// <summary>What feeding a pull-down's new albums into the potentials shortlist did.</summary>
/// <param name="Added">Albums that weren't on the shortlist and are now waiting to be decided on.</param>
/// <param name="PreviouslyDropped">
/// Albums in the playlist that were dropped from the shortlist. They're named rather than silently
/// skipped — a ruled-out album still sitting in the listening queue is worth knowing about — but
/// nothing is done to them: a decision isn't reopened by the album still being there.
/// </param>
/// <param name="Problem">Why the shortlist couldn't be read or saved, or null if all went well.</param>
/// <param name="Waiting">
/// Every candidate this playlist points at that's still undecided — the ones just added plus those
/// already on the shortlist. This is the set the year lookup works over, so an album that landed
/// yearless on an earlier pull gets another chance at one.
/// </param>
/// <param name="YearsFound">How many of those years Discogs supplied on this run.</param>
public sealed record ShortlistIntakeResult(
    IReadOnlyList<CandidateAlbum> Added,
    IReadOnlyList<CandidateAlbum> PreviouslyDropped,
    string? Problem,
    IReadOnlyList<CandidateAlbum>? Waiting = null,
    int YearsFound = 0)
{
    public static ShortlistIntakeResult Nothing { get; } =
        new(Array.Empty<CandidateAlbum>(), Array.Empty<CandidateAlbum>(), null);

    public static ShortlistIntakeResult Failed(string problem) =>
        Nothing with { Problem = problem };

    /// <summary>Candidates from this playlist still without a year — someone has to type those in.</summary>
    public IReadOnlyList<CandidateAlbum> StillWithoutAYear =>
        (Waiting ?? Array.Empty<CandidateAlbum>()).Where(a => a.Year.Trim().Length == 0).ToList();

    /// <summary>
    /// One sentence for the pull-down's status line, or "" when there's nothing worth saying —
    /// albums already on the shortlist are the common case and pass without comment.
    /// </summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();

            if (Added.Count > 0)
                parts.Add($"{Added.Count} added to the shortlist: {Names(Added)}.");

            // Said on every pull for as long as the album sits in the playlist, so it's phrased as
            // the standing state of things rather than as something that just happened.
            if (PreviouslyDropped.Count > 0)
            {
                string is_ = PreviouslyDropped.Count == 1 ? "is" : "are";
                parts.Add($"{Names(PreviouslyDropped)} {is_} in the playlist but dropped from the shortlist — not put back.");
            }

            if (YearsFound > 0)
                parts.Add($"{YearsFound} year{(YearsFound == 1 ? "" : "s")} filled in from Discogs.");

            int blank = StillWithoutAYear.Count;
            if (blank > 0)
                parts.Add($"{blank} still {(blank == 1 ? "has no year" : "have no year")} — type {(blank == 1 ? "it" : "them")} in on the shortlist.");

            if (Problem is not null) parts.Add($"⚠ {Problem}");

            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// The albums by name, but only the first few — a pull can bring in a dozen at once, and a
    /// status line listing all of them fills the phone's screen. The count above already says how
    /// many; the names are there so the user recognises what arrived.
    /// </summary>
    private static string Names(IReadOnlyList<CandidateAlbum> albums)
    {
        const int Shown = 4;
        var named = string.Join(", ", albums.Take(Shown).Select(a => $"{a.Title} — {a.Artist}"));
        return albums.Count > Shown ? $"{named} and {albums.Count - Shown} more" : named;
    }
}

/// <summary>
/// The route from Playlist 2 back into the potentials shortlist.
/// <para>
/// Playlist 2 is the recommendations queue, so an album sitting in it that the shortlist has never
/// heard of is by definition a potential replacement nobody wrote down. Every pull of Playlist 2
/// hands the <em>whole</em> playlist here, and anything missing lands as a pending candidate,
/// exactly as if it had been typed into "Add a potential".
/// </para>
/// <para>
/// Deliberately the whole playlist and not just the pull's new arrivals. "New" means new to the
/// working list, which is a fact about one device: pull on the phone and the Mac's next pull sees
/// nothing new, so an album that reached neither shortlist stays lost — which is exactly what
/// happened on 2026-08-21. Comparing against the shortlist itself has no such memory, so it comes
/// out the same on either device and however many times it's run.
/// </para>
/// <para>
/// A decision already made is never undone: an album that was kept, is still waiting, or was
/// dropped is left exactly as it is. That's the shortlist's own rule — the file is the record of
/// what's been ruled on, not just what's left — and it's what stops a dropped album that's still
/// sitting in Apple Music from being offered again on every sync.
/// </para>
/// </summary>
public static class ShortlistIntake
{
    /// <summary>
    /// Appends every album in <paramref name="albums"/> that isn't already on
    /// <paramref name="shortlist"/> to it as a pending candidate, mutating the list in place.
    /// <para>
    /// Titles are compared the way <see cref="PlaylistStore.SyncFromAppleMusic"/> compares them, not
    /// the way the shortlist normally does: these names come from Apple Music, which calls an album
    /// by whichever edition it stocks, so on the sheet's stricter rule the catalog's "Tago Mago
    /// (2011 Remastered)" would read as a different record from our "Tago Mago" and earn a second row.
    /// </para>
    /// </summary>
    public static ShortlistIntakeResult Merge(List<CandidateAlbum> shortlist, IEnumerable<PlaylistEntry> albums)
    {
        var added = new List<CandidateAlbum>();
        var dropped = new List<CandidateAlbum>();
        var waiting = new List<CandidateAlbum>();

        foreach (var album in albums)
        {
            // Classified against the growing list, so the same album twice in one playlist — Apple
            // Music can hold two editions of it — still only makes one row.
            var (outcome, match) = ReplacementCandidates.Classify(shortlist, candidate =>
                DiscogsApiClient.TitlesLineUp(candidate.Title, album.Title)
                && NumberedList.Matches(candidate.Artist, album.Artist));

            if (outcome == CandidateAddOutcome.New)
            {
                // No genre and often no year: Apple Music's read-back has neither. The year gets
                // filled in by the shortlist's own Discogs prefetch, or on the way to being kept.
                //
                // The name is the album's, not the pressing's: Apple Music stocks "Cassini (5th
                // Anniversary Remaster)", and a shortlist row that's kept becomes a line on the
                // replacements list, where nobody wants the edition furniture. It's the same
                // preference a pull-down already shows when it keeps our name over the catalog's —
                // this is just what that means when we have no name of our own yet. Dropping the
                // suffix costs nothing on the way back either: the next pull matches on a
                // whole-word prefix, so the shortened title still recognises the album.
                var candidate = new CandidateAlbum
                {
                    Title = DiscogsApiClient.BareTitle(album.Title),
                    Artist = album.Artist,
                    Year = album.Year,
                    Status = CandidateStatus.Pending,
                };
                shortlist.Add(candidate);
                added.Add(candidate);
                waiting.Add(candidate);
            }
            else if (outcome == CandidateAddOutcome.Reopen)
            {
                dropped.Add(match!);
            }
            else if (outcome == CandidateAddOutcome.AlreadyPending)
            {
                waiting.Add(match!);
            }
        }

        return new ShortlistIntakeResult(added, dropped, null, waiting);
    }

    /// <summary>
    /// Loads the shortlist, merges the playlist into it, and saves it back — to the
    /// local cache where there is one, and to Google Sheets whenever sync is configured, so the
    /// new candidates reach the other device too.
    /// </summary>
    /// <param name="cacheLocally">
    /// True on the desktop, which keeps the shortlist in a local JSON file; false on the phone,
    /// where there's no writable copy and Sheets is the only place a candidate can be saved.
    /// </param>
    /// <param name="progress">
    /// Called with a line for the caller's status area while the Discogs lookups run — a dozen of
    /// them take a dozen seconds, and a pull that looks stalled is worse than one that counts.
    /// </param>
    public static async Task<ShortlistIntakeResult> AbsorbAsync(
        IEnumerable<PlaylistEntry> albums, bool cacheLocally, Action<string>? progress = null)
    {
        var incoming = albums.ToList();
        if (incoming.Count == 0) return ShortlistIntakeResult.Nothing;

        var repo = CandidateRepository.Create();
        if (!repo.SyncEnabled && !cacheLocally)
            return ShortlistIntakeResult.Failed(
                $"Sheets sync {repo.Status} — new albums couldn't be added to the shortlist.");

        List<CandidateAlbum> shortlist;
        try
        {
            shortlist = await LoadAsync(repo, cacheLocally);
        }
        catch (Exception ex)
        {
            // Better to add nothing than to save a stale shortlist over a live one.
            return ShortlistIntakeResult.Failed($"Couldn't read the shortlist — {ex.Message}");
        }

        var result = Merge(shortlist, incoming);

        // Apple Music's read-back carries no year, so a candidate from a playlist arrives without
        // one — and a year is what the shortlist sorts by and what keeping an album demands. The
        // lookup covers every undecided candidate this playlist points at, not just today's
        // arrivals, so albums that landed yearless on an earlier pull are picked up too.
        int years = await FillYearsAsync(result.Waiting!, progress);
        result = result with { YearsFound = years };

        if (result.Added.Count == 0 && years == 0) return result; // nothing changed, nothing to write

        string? problem = null;

        if (cacheLocally)
        {
            try { ReplacementCandidates.Save(shortlist); }
            catch (Exception ex) { problem = $"Couldn't save the shortlist — {ex.Message}"; }
        }

        if (repo.SyncEnabled)
        {
            try { await repo.PushAsync(shortlist); }
            catch (Exception ex) { problem = $"Shortlist saved here, but Google Sheets sync failed — {ex.Message}"; }
        }

        return result with { Problem = problem };
    }

    /// <summary>
    /// Discogs allows 60 requests a minute against a token, so lookups are spaced a shade wider than
    /// one a second — the same pacing the shortlist window's own prefetch uses, and for the same
    /// reason: a 429 comes back looking exactly like "no year found", and would stick.
    /// </summary>
    private static readonly TimeSpan LookupSpacing = TimeSpan.FromMilliseconds(1100);

    /// <summary>
    /// Fills in the years Apple Music didn't supply, from Discogs, and returns how many were found.
    /// Does nothing where no Discogs token is configured; one album failing to resolve doesn't stop
    /// the rest, and anything still blank is left for someone to type in on the shortlist.
    /// </summary>
    public static async Task<int> FillYearsAsync(
        IReadOnlyList<CandidateAlbum> candidates, Action<string>? progress = null)
    {
        var missing = candidates.Where(a => a.Year.Trim().Length == 0).ToList();
        if (missing.Count == 0) return 0;

        var discogs = DiscogsApiClient.TryCreate();
        if (discogs is null) return 0;

        int found = 0;
        for (int i = 0; i < missing.Count; i++)
        {
            if (i > 0) await Task.Delay(LookupSpacing);
            progress?.Invoke($"Looking up years… {i + 1}/{missing.Count}");

            try
            {
                var year = (await discogs.FindAlbumAsync(missing[i].Title, missing[i].Artist))?.Year;
                if (!string.IsNullOrWhiteSpace(year)) { missing[i].Year = year; found++; }
            }
            catch
            {
                // One album Discogs can't answer for isn't a reason to abandon the other eleven.
            }
        }

        return found;
    }

    /// <summary>
    /// The shortlist as it stands: the Google Sheet when it's configured and has anything on it,
    /// otherwise the device's own copy. That asymmetry is the same one the shortlist windows use —
    /// an empty sheet means "not seeded yet", never "the shortlist is empty", so it must not be
    /// allowed to wipe what we hold. A sheet that fails to load throws instead, so the caller can
    /// abandon the intake rather than push a stale list over a live one.
    /// </summary>
    private static async Task<List<CandidateAlbum>> LoadAsync(CandidateRepository repo, bool cacheLocally)
    {
        if (repo.SyncEnabled)
        {
            var remote = await repo.PullAsync();
            if (remote is { Count: > 0 }) return remote;
        }

        // The phone has no writable copy, so its baseline is the snapshot baked into the app —
        // the same list its Shortlist tab opens on before Sheets answers.
        return cacheLocally ? ReplacementCandidates.Load() : MobileData.LoadCandidates();
    }
}
