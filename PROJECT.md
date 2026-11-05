# 1001 Albums Helper — Project Handbook

A living reference for the app: what it is, how it's built, how to ship it, and where it's going.
**Update this as we go.** Last updated: 2026-11-04 (exact artist matches first; two removes on Playlist 2; Insights rebuilt).

---

## 1. What it is

A personal tool for working through the **"1001 Albums You Must Hear Before You Die"** list —
rating albums, curating a shortlist of *potential replacement* albums, and building listening
playlists. It runs as:

- **A Mac desktop app** (the original) — multi-window, does the heavy lifting: Discogs lookups,
  Google Sheets sync, PDF/Excel/CSV export.
- **An iPhone app** (newer) — a focused, on-the-go companion: browse the list, build playlists,
  push them to Apple Music.

Both are built from **one shared C# codebase** using **Avalonia** (a cross-platform .NET UI
framework).

---

## 2. Architecture (brief)

```
1001AlbumHelper.sln
├── 1001AlbumHelper/            → shared library  (assembly: 1001AlbumHelper.Core.dll)
│   ├── App.axaml               Avalonia application (runs windowed OR single-view)
│   ├── Models/                 Album, AlbumSuggestion, CandidateAlbum, ViewRow
│   ├── Data/                   NumberedList, ReplacementCandidates, MobileData,
│   │                           PlaylistStore, ShortlistIntake, Operations, RatingSession,
│   │                           AlbumProcessor
│   ├── Integrations/           DiscogsApiClient, GoogleSheetsWriter, CandidateSheet,
│   │                           CandidateRepository, AppleMusicCatalog, PlaylistTracks,
│   │                           SyncDiagnostic,
│   │                           ISheetsClient (+ RestSheetsClient, GoogleServiceAccountAuth,
│   │                           RestSheetsClientDiagnostic) — the REST path for mobile writes,
│   │                           MobileSheets — the phone's client for the *master* spreadsheet
│   ├── Export/                 CsvGenerator, ExcelGenerator, PdfExporter
│   ├── Views/Desktop/          the seven desktop windows
│   ├── Views/Mobile/           MainView + AlbumListView, RateView, ReplacementsView, PlaylistView
│   ├── Console/                ConsoleMenu (text menu; `dotnet run -- console`)
│   └── input/ output/ appsettings.json  (data + config; git-ignored where secret)
│
├── 1001AlbumHelper.Desktop/    → Mac/Win/Linux head (produces the "1001AlbumHelper" executable)
│                               also: MusicAppPlaylistWriter (Apple Music on the Mac) +
│                               MusicAppDiagnostic (its `musictest` check) +
│                               ShortlistIntakeDiagnostic (the read-only `intaketest` rehearsal) +
│                               FavoritesBackfillDiagnostic (the read-only `favtest` rehearsal)
├── 1001AlbumHelper.iOS/        → iPhone head (bundle id: com.larkins.albumhelper)
└── 1001AlbumHelper.Tests/      → xUnit tests (300 as of this writing)
```

**Key idea:** all the real code lives in the shared library. The two "head" projects are thin —
just an entry point plus the platform's Avalonia backend. Desktop shows **windows**; iPhone shows
a **single view with four bottom tabs** (phones don't do windows).

**Where data comes from**

| Source | Used by | Notes |
|---|---|---|
| Google Sheets | Desktop | The master lists + the potentials sync (service account). |
| Discogs API | Both | Album/year lookups. Token in `appsettings.json`. |
| Embedded CSV/JSON snapshots | iPhone | Baked into the app so it works offline (`MobileData`). |
| Apple Music (MediaPlayer) | iPhone | Add albums to playlists. See §5. |
| Apple Music (Music.app / AppleScript) | Mac | Same two playlists, the Mac way. See §5. |
| iTunes Search API | Both | Store IDs for playlist adds, and **cover art** for the Rate screens (`AlbumArtwork`). No auth. |
| Local JSON | Both | `replacement-candidates.json`, per-device playlist working lists. |

---

## 3. Version history / iterations

| Date | Milestone |
|---|---|
| ≤ 2026-07-21 | **Desktop app.** Discogs lookups, the 1001 list, ratings, potentials shortlist, Google Sheets export, PDF/Excel/CSV. |
| 2026-07-24 | **iPhone port begins.** Split the single project into shared library + Desktop/iOS heads. Mac app unchanged. |
| 2026-07-25 | **Mobile app.** Four-tab UI (List · Replacements · Playlist 1 · Playlist 2), offline data snapshots. |
| 2026-07-25 | **Folder reorg.** Grouped the shared project into Models/Data/Integrations/Export/Views/Console. |
| 2026-07-25 | **Apple Music export** (first pass — created its own named playlists). |
| 2026-07-28 | **Cross-device potentials sync** via a Google service account (Potentials tab). Desktop wired + verified. |
| 2026-07-28 | **On-device deploy.** App signed + installed + running on the real iPhone 15 Pro. |
| 2026-07-28 | **Bug fix:** Sheets writes use RAW so numeric album titles (2112, 1984) aren't mangled into numbers/dates. |
| 2026-07-28 | **Apple Music → your real playlists.** ＋P1/＋P2 and "Push all" target the existing `PLAYLIST1`/`PLAYLIST2` by name; "Import" reads them. Adding is confirmed working on-device. |
| 2026-07-28 | **One-click re-deploy.** `Re-deploy to iPhone.command` renews the 7-day profile + rebuilds + reinstalls. |
| 2026-07-28 | **Mobile Sheets sync, take 2.** Rewrote `CandidateSheet` onto the Sheets REST API (signed service-account JWT, RSA) — **no Google.Apis**, so it's trim/AOT-safe on iOS. Verified on desktop. |
| 2026-07-28 | **iOS fix:** `Operations.ResolveDataDir`'s directory walk no longer crashes the type initializer when it hits the iOS sandbox ("Operation not permitted"). This was the real blocker for mobile sync. |
| 2026-07-28 | **Mobile Sheets sync confirmed on-device.** The four stacked bugs above (trimming, Google.Apis, the REST rewrite, the sandbox crash) are all fixed together — the Replacements tab reads live from Google Sheets on the phone. |
| 2026-07-29 | **Apple Music playlist UX (Phase 3).** Pushing to Apple Music now names each failed album + why, instead of just a count. Removing an album that's already in Apple Music no longer silently drops it — it moves to a "delete these from Apple Music" checklist (Apple's API can't remove for us) until the user confirms they deleted it by hand. Reading a playlist back now carries each album's track count, and a low count is flagged in the UI as possibly half-deleted. |
| 2026-07-29 | **Mobile: add to shortlist + edit years.** `ReplacementsView` gained an inline add-album panel (Discogs autocomplete lookup, reusing the desktop's `AlbumLookup` helper) and in-place year editing, both pushing straight to the Potentials sheet. Needed a Discogs token on iOS, so `DiscogsApiClient` picked up the same embedded-config fallback `CandidateRepository` already had (factored into a shared `EmbeddedConfig` helper). |
| 2026-07-29 | **Mobile: rate albums.** New "Rate" tab — the desktop rating window's one-album-at-a-time queue (⭐/👍/👎/❌), writing straight to the master "1001 albums" sheet. Needed a REST path for the *master list* too, not just Potentials: extracted `ISheetsClient` from `GoogleSheetsWriter` and added `RestSheetsClient`, a full REST reimplementation (including the insert-row-with-formatting call the ⭐→Must Hear side effect depends on). Verified end-to-end against the live spreadsheet via a new self-cleaning `resttest` diagnostic (scratch tab, never touches real data) before wiring up the UI. |
| 2026-07-29 | **Mobile: browse Must Hear + Replacements.** The "List" tab gained a 1001 / Must Hear / Replacements switcher instead of two more bottom tabs — Must Hear and Replacements read live via the same `RestSheetsClient` the Rate feature added, no new plumbing needed. **Phase 2 is done.** |
| 2026-07-29 | **iOS app icon.** Reused the Mac app's "1001 + music note" art, cropped past its baked-in macOS rounding into a flat opaque square, as a single-size `Assets.xcassets/AppIcon.appiconset`. The home screen no longer shows a blank placeholder. |
| 2026-07-29 | **Bug fix:** Apple Music catalog lookup missed real albums Apple's own `/search` buries in its relevance ranking (reproduced live: zero results for "Nine Inch Nails The Downward Spiral"). Falls back to the artist's full catalog via `/lookup` when `/search` comes up empty. (Recorded on the `iphone-support` branch at the time; the row reached this table when that branch was merged on 2026-11-04.) |
| 2026-08-04 | **Two bug fixes, both apps.** (1) Browsing the **Replacements** list offered "＋ P1", so recommendations queued into PLAYLIST1; the ＋ button now follows the list you're in (1001/Must Hear → P1, Replacements → P2) on both `AlbumListView` and `ListViewerWindow`. The label and pressed-state moved onto `ViewRow` itself, since these lists virtualize and a label set directly on the button rode its recycled container onto other rows. (2) **Mobile had no way to keep or drop a candidate** — `ReplacementsView` gained Keep/Nah/Undo matching `CandidatesWindow`, and now hides decided rows like desktop does. Keep writes to the *master* spreadsheet, which the phone couldn't reach: `Operations.AddReplacementAlbumAsync` gained an `ISheetsClient` overload, and the config-and-key dance `AlbumListView` did inline became `MobileSheets`. |
| 2026-08-14 | **Cover art on the Rate screens** (Mac *and* iPhone). The album card now shows the sleeve, fetched through the same `AppleMusicCatalog` lookup the playlist export uses — so the art shown is the art of the album we'd actually add to a playlist. New `AlbumArtwork` caches decoded covers in memory and the downloaded JPEGs on disk, and warms the *next* album's art while you're rating the current one, so it's usually already there. A "♫" sleeve placeholder holds the space while a lookup runs (and for albums with no art), so the card never jumps between two heights. The desktop window grew 600→680 tall to fit it. |
| 2026-08-19 | **Apple Music playlists on the Mac.** The Mac could queue albums into Playlist 1/2 but had nowhere to see them and no way to push them — `AppleMusic.Writer` was set only by the iOS head. Added `MusicAppPlaylistWriter`, which drives **Music.app over AppleScript** (`osascript`), and a `PlaylistWindow` with the phone's Import / Push all / remove / "delete these" checklist. Where iOS resolves a store id via the iTunes Search API and adds by id, the Mac asks Music to `search` and duplicates the resulting tracks — and with Sync Library on that search reaches the **whole Apple Music catalog**, so obscure 1001 entries resolve just like on the phone, with no iTunes rate limit in play. Album choice runs through the very same `AppleMusicCatalog.FindBestMatch` the phone uses, so both apps prefer the original release over a "(Deluxe Edition)" identically. Track→album folding moved into a shared `PlaylistTracks` both writers use. Verified end-to-end by a new self-cleaning `musictest` diagnostic (scratch playlist, created and deleted, never touches PLAYLIST1/2). |
| 2026-08-19 | **Import became a pull-down sync** (both apps). It used to *merge* — add what Apple Music had, never remove — so the working list only ever grew. Now the Apple Music playlist is the source of truth: after a pull the list holds exactly what that playlist holds, and anything queued locally but not in Apple Music is dropped. Two things deliberately survive: albums on the **"delete these" checklist stay on it** rather than being resurrected as active (Apple Music still having them is the whole reason they're listed — a merge-back would undo the user's removal on every sync), and a known album's **year is carried across**, since Apple Music's read-back has no year and our lists do. The nice inverse: a checklist album that's *gone* from Apple Music means the user has now deleted it by hand, so the sync ticks it off. Matching had to loosen to `DiscogsApiClient.TitlesLineUp` — with strict matching the catalog's "Tago Mago (2011 Remastered)" read as a different record from our "Tago Mago", so every sync would drop ours, re-add theirs, and lose the year. Buttons relabeled Import → **Pull down**. |
| 2026-07-29 | **Mac app parity pass.** Added a "Renew iPhone trial" button to `MainWindow` (shells out to `deploy-to-device.sh`, streaming its output into the activity log — see §4/§7) and "+P1"/"+P2" playlist buttons to `ListViewerWindow` (browse) and `CandidatesWindow` (potential replacements), matching the mobile app's `AlbumListView`/`ReplacementsView`. Both apps rebuilt, reinstalled, and committed (`f852195`). |
| 2026-08-21 | **Changing a rating you already gave.** Ratings used to be write-once in practice: both queues (`NextUp`, `Backfill`) are defined by a rating being *absent*, so a rated album never came up again and only `Back` within a live session could correct a mis-tap. Added a third queue, **`RatingMode.Revisit`** — albums that already carry one of the four symbols, optionally narrowed to one of them — reachable from a new "Change a rating" card on `MainWindow` and a Revisit segment in `RatingWindow`, whose card now says what the album is rated now. `RatingSession.FocusOn(sheetRow)` points a session at one album whatever its rating (splicing it into the queue if it isn't there), which is how the browse list's new per-row ✎ button opens the rater straight onto that album — the window hands its changes back by sheet row so the list updates without re-reading the sheet. On the phone, where there's no queue picker, the Rate tab gained a **find box** over `RatingSession.Search` that jumps to any album by name. The Must Hear side effect became symmetric: gaining a ⭐ still adds, and **losing one now removes** (via `NumberedList.ApplyAsync`, which renumbers what's left) so a downgraded album doesn't sit on a list it no longer belongs on. |
| 2026-08-21 | **Two rating fixes.** (1) **Mobile had no queue picker** — the Rate tab always walked "not yet listened", so the ✓ backfill the Mac offers was unreachable on the phone. It now has the same three queues (Next up · Backfill ✓ · Revisit, with Revisit's symbol filter); shuffle stays Mac-only. (2) **Lists didn't catch up with a rating just given.** The phone's 1001 tab renders a snapshot baked into the app at build time, so a rating made on the Rate tab still showed its old symbol next door — and ratings given on the Mac never showed at all. Two halves: `RatingChanges` records every save this run (keyed loosely on title+artist, since snapshot rows carry no sheet row) and cached lists apply it when shown; and the 1001 tab now opens on the snapshot as before, then **replaces it with the live sheet** in the background, so it stays instant and offline-safe but no longer stale. On the Mac, the browse window drops its cached Must Hear after a rating, since a ⭐ given or taken away rewrites that list. |
| 2026-08-21 | **Playlist 2 pull-downs feed the shortlist.** Albums queued into PLAYLIST2 by hand in Apple Music used to arrive as playlist rows and stop there — a recommendation the shortlist never heard about, so it could never be kept, dropped, or synced to the phone. A pull of Playlist 2 (and only Playlist 2 — Playlist 1 is the 1001 itself) now hands its new arrivals to a shared `ShortlistIntake`, which adds each one to the potentials shortlist as a pending candidate and saves it the way that list is saved on the device: local JSON plus Google Sheets on the Mac, Sheets alone on the phone. `PlaylistSyncResult` had to start naming the albums it took in rather than counting them. A decision already made is never undone — kept, waiting and dropped albums are left exactly as they are, since the shortlist is the record of what has been ruled on — though a dropped one is named in the status line rather than silently skipped. Matching uses the pull-down’s own loose title rule, not the sheet’s stricter one, or the catalog’s “Cassini (5th Anniversary Remaster)” would earn a second row beside our “Cassini”; a new candidate is stored under the album’s name rather than the pressing’s for the same reason. Rehearse a pull without writing anything: `dotnet run --project 1001AlbumHelper.Desktop -- intaketest`. |
| 2026-08-21 | **A save could wipe the shortlist — fixed.** The intake above surfaced it immediately: a Playlist 2 pull put twelve new candidates on the Potentials sheet, and the next Keep on the phone’s Shortlist tab deleted them again. Two causes, both fixed. (1) **`PushAsync` replaced the whole sheet.** Every screen holds the shortlist for as long as it’s open and is never told when the other device changes it, so the older copy overwrote the newer one. Nothing in the app ever *deletes* a candidate — rows are appended, and a decision is a status change on a row that stays — so a write that shortens the list is always a stale copy winning. The push now reads the sheet and folds the caller’s rows into it (`ReplacementCandidates.Merge`): the writer’s row wins where both know an album, so decisions and undos still travel, but a year or genre comes from whichever side has one, and rows the writer never heard of survive. (2) **The phone’s Shortlist tab never reloaded** once visited, so it sat on the list it loaded at launch; it now refreshes when the tab comes forward, like the playlist tabs already did. |
| 2026-08-21 | **The intake compares against the shortlist, not against "what's new".** The first cut only offered up albums a pull found new to that device's working list, and that turned out to be the wrong question: pulling on the phone consumed the newness, so the Mac's next pull saw nothing new and twelve albums that had reached neither shortlist stayed stranded. Every pull now hands the **whole** playlist over and adds whatever the shortlist doesn't already hold, which has no per-device memory in it — same answer on either device, however many times it runs. A dropped album still sitting in the playlist is named on each pull rather than re-offered, so that line is phrased as a standing state ("in the playlist but dropped from the shortlist") rather than as news. |
| 2026-08-21 | **Candidates from a playlist arrive with a year.** Apple Music's read-back carries no year, so albums taken off Playlist 2 landed blank — and a year is what the shortlist sorts by and what keeping an album demands. The Mac's shortlist window has always back-filled years from Discogs, but only once you opened it, and the phone had no such thing at all. The intake now looks them up as part of the pull, paced at the same 1.1s the window's prefetch uses (a 429 reads as "no year found" and sticks), counting its way down the status line since a dozen lookups take a dozen seconds. It covers **every undecided candidate the playlist points at**, not just today's arrivals, so albums that landed yearless on an earlier pull are picked up on the next one. Anything Discogs can't answer for is named as still needing a year rather than passing quietly. |
| 2026-08-21 | **Picking the copy of a song that actually plays.** Apple Music stocks some albums twice under identical names, and the copies aren't equally alive: Method Man's *Tical 2000: Judgement Day* is there twice, and five tracks are "no longer available" on one of them while the other plays them fine. Nothing in the metadata separates the two — same album name, same artist, same track numbers, same release date; only the durations differ, by hundredths of a second — so there's no better edition to pick, and the choice has to be made a song at a time. The Mac's search now carries each track's **cloud status**, disc and track number, and `PlaylistTracks.PreferPlayable` takes one copy per slot, preferring one that will play. That also fixes the plainer bug underneath: both listings matched the search, so the album was going in twice over — 56 tracks for a 28-track record, five of them dead. A song no copy can play is now named in the push result rather than quietly missing. `musictest` pushes this album as a regression check and fails if it comes back anywhere near 56 tracks. **The iPhone is untouched:** the iTunes Search API reports one collection for this album and no playability at all, so there is nothing there to choose between. |
| 2026-08-21 | **The phone app gets a design system.** The mobile UI was stock Avalonia Fluent — gray buttons, a solid-blue selection bar down a 1000-row list, and each screen redefining its own “segmented control” out of opacity tweaks. All of it now comes from one file, `Views/Mobile/MobileTheme.axaml`: palette (near-black canvas, the gold the Keep button already used as the single accent), an Inter type scale, and a look for every control. It is included by **`MainView`, not `App.axaml`** — App is shared with the Mac head, so that scoping is what lets the phone be restyled without touching one desktop window. The **nav** was rebuilt rather than restyled: the TabControl's own strip is hidden (`headless`) and a real bottom bar drawn in its place, icon over label with a gold pill on the active tab, while the TabControl still switches content so `SelectedIndex` and every per-tab refresh are unchanged. Three grid bugs fell out of doing it: row padding collapsed to zero because Fluent **template-binds `ListBoxItem.Padding`**, which beats a style setter on the row's ContentPresenter — the ＋ buttons were being clipped off the right edge, and the inset is set through the `ListBoxItemPadding` resource now; browse rows used an `Auto` rating column, so an unrated album's title sat left of its neighbors'; and the four rating tiles were visibly uneven because “Mostly enjoyable” wraps to two lines where “Trash” doesn't. A sync line that reads “” on a good sync was reserving its line height and leaving a gap above every list, so those collapse when empty. `mobilepreview` gained `ALBUMHELPER_PREVIEW_TAB` — without it every screen but List needs a tap to reach, which is awkward when the point of the run is to look at one of the others. |
| 2026-08-21 | **An album is dated from its first release, not from a reissue.** A 1970 record remastered in 2010 should read 1970. The Discogs lookups already searched `type=master`, which is the original in the ordinary case — but Discogs files a reissue under an *extended name* (“Abbey Road (2019 Mix)”), so that is a master of its own, and when the playlist names the pressing it holds, the first rung of the search ladder finds exactly it. `PreferOriginalRelease` now sits over the verified candidates: where one candidate's title is a whole-word prefix of another's, the shorter is the album and the longer an edition of it, and the edition is set aside. **Deliberately not “take the earliest year”** — `AlbumRankingTests` already documents why that is wrong: Blue Train has masters at 1957 (a recording-date entry), 1958 (the release everyone owns) and 2010 (a reissue), and 1958 is the answer. Most-owned still decides between same-named masters; the new rule only fires when one title extends another, so that test is untouched. Five tests added. |
| 2026-08-25 | **Finding an album to rate stopped happening at the bottom of the screen.** The Rate tab's find box sat in the last row of the layout, which on a phone is precisely where iOS puts the keyboard: tapping it hid the box, the matches it was producing *and* the four rating tiles, so you typed a name blind with nothing to pick from. The field moved **into the pinned header**, under the queue picker, where it is always on screen and the keyboard opens below it; the results take the album card's place rather than being buried under glass. A first cut put the field behind a "Jump to…" chip instead and that was wrong — a search you have to go and find reads as a search that was removed. Beside the field an **A–Z button** opens those same results *without* raising the keyboard, which is what makes the new **index rail** worth having: an empty field lists all 1089, and the rail down the right edge works like the index in Contacts — press and drag and the list skims, with the stop under your thumb shown in a bubble over the list (in the gutter your own finger would cover it). The rail indexes whatever order the list is in — initials for Title A–Z and Artist A–Z, hundreds for list order — takes each label once at its first row (symbol-led titles sort both before "A" and after "Z", and two "#" stops read as a broken rail), and offers only stops with rows behind them, so a drag can't land on nothing. A `Viewbox` shrinks the strip to whatever height is left when the keyboard *is* up. Filing follows `NumberedList.Normalize`, which already drops a leading "the", so The Beatles sits under B. |
| 2026-10-30 | **Albums missing from the Music library get a way in.** A Mac push reported "Not found in Music: Dummy" for an album plainly in Apple Music. The cause wasn't the matching: `search library playlist 1` only reaches the *library*, never the catalog — the Sync Library claim from 2026-08-19 was wrong — and AppleScript can't add a catalog album at all. Now a library miss falls back to the iPhone's `AppleMusicCatalog` lookup; if Apple Music has the album, the failure row gets an **Open in Music** button (a `music://` link to its page) so one ＋ there and another push finishes the job. The message also says which it is — not in the library, or not in Apple Music at all. Alongside: the Playlists window's album titles now trim instead of running under the artist column (a horizontal StackPanel never constrains its children), its headings line up with the rows again, and **Browse lists opens the 1001 at the next album to rate** — the one after the furthest rated row — rather than at #1. |
| 2026-11-03 | **One window, and whole albums from the catalog.** The Mac app was a dashboard of eleven cards, each opening its own modal; it's now **one window with a sidebar** — Rate · Browse · Shortlist · Playlists · More, the phone's sections — with each shown in place (`IPage`; pages are built on first visit and kept). Rate is the home page; adding an album, favorites, analytics, export, sync, "Check connection" and the activity log (now fed by all app output) live under More; ⌘1–⌘5 switch. The former windows became `RatePage`, `BrowsePage`, `ShortlistPage`, `PlaylistsPage`; Browse's ✎ opens a `RatePage` in a `PageWindow`. "Renew iPhone trial" is gone from the UI (the phone app is retired; its code and scripts stay). Playlists: "Pull down / Push all" became **Get from Apple Music / Send new to Apple Music**, rows say "not sent yet", and sending skips albums already in the playlist (it used to add them again). Browse opens at the first unrated album, matching Rate's Next up. And the add itself was rebuilt: the release is chosen in the **catalog** (original › remaster › deluxe, never a single or a sequel), added to the library by pressing Music's own "Add to Library", and copied whole — the old library-based pick had put one song of an album into PLAYLIST2. See §5. |
| 2026-11-03 | **Remove means remove — and on Playlist 2, "no thanks".** On the Mac, ✕ Remove now takes the album out of the Music playlist itself (`IApplePlaylistWriter.RemoveAlbumAsync`; deleting a playlist's track leaves the library copy), instead of parking it on a "delete these by hand" checklist — the phone's MediaPlayer is add-only, the Mac's Music app isn't. Matching for a delete is deliberately stricter than the pull-down's prefix rule, so "Tical" can't take "Tical 2000" with it. On Playlist 2 a removal could also be a verdict (since 2026-11-04 that's a separate **Remove + drop** button): `Operations.DropFromShortlistAsync` marks the album dropped on the shortlist (adding it as dropped if it wasn't there), so it's never offered or re-absorbed again — unless it was already kept, which stays kept. The old checklist's "✓ Deleted" became "Remove from Apple Music", which deletes and clears. `musictest` checks a removal leaves the library alone. Same day, `PlaylistStore.SameAlbum` dropped its whole-word prefix rule for `AppleMusicCatalog.SameRelease` (edition tags and a ": subtitle" set aside, nothing else): with Tical 2000 on the removal checklist, a pull bringing in the real Tical had filed it under that row, so Tical never appeared. |
| 2026-11-03 | **More split up; Export gets a page of its own.** The sidebar is now Rate · Browse · Shortlist · Playlists · **Export** · **Insights**, with **Tools** apart at the bottom (⌘1–⌘7). Export is the finished list and the point of the app: the Must Hear / Replacements / total counts (re-read from the sheet on each visit), the sync status with its button, and Export PDF with "Show in Finder". The Analytics window became `InsightsPage`, still built in the Desktop head for LiveCharts' sake (`InsightsPageHost` hands the shared library a `Control`). "Add an album" moved to a button in Browse; Tools keeps the favorites backfill, the connection check and the activity log. A "Get from Apple Music" pull now fills blank years — from the lists, then Discogs — since Music's read-back has none. |
| 2026-11-03 | **Standalone.** The installed app no longer needs the source checkout. Data moved to `~/Library/Application Support/1001AlbumHelper` (it was the project folder, which the launcher pointed at), exports to `~/Documents/1001 Albums Helper`; settings and the service-account key come from the copies built into the app, overridden by any in the data folder. **One Google auth**: the master-list client signs in as the service account rather than via browser OAuth, so no sign-in, no token to expire, no `credentials.json`. The shortlist's local copy left the repo and git (the Potentials sheet was already the record). The bundle runs the apphost directly and is **signed with the Apple Development certificate** so macOS permissions survive rebuilds. Also: the master list's shape is checked on load (a shifted column refuses to open for rating rather than writing into the wrong cell), Browse and Export fall back to a snapshot of the last good read when offline, and Avalonia went 11.3.0 → 11.3.22, which clears the Tmds.DBus advisory. |
| 2026-11-04 | **The artist's own name wins, and Playlist 2 has two removes.** Adding America's *Homecoming* opened "The Homecoming" by *Midnight in America* instead: `ArtistsOverlap` counts a shared word ("america") as the same act, and that listing's 7 tracks beat the real one's 10 on the fewest-tracks tie-break. `AppleMusicCatalog.FindBestMatch` now takes exact artist matches first and falls back to word overlap only when nothing matches exactly, and `FindAlbumAsync` trusts its first /search hit only when the artist matches exactly, so an overlap-only hit still triggers the artist-catalog lookup. On Playlist 2, **✕ Remove** now takes the album out of the playlist only; **✕ Remove + drop** also marks it dropped on the shortlist (the old single button always did both). |
| 2026-11-04 | **Insights rebuilt, and the math put right.** The favorite genre was a coin toss: ratings run high, so nine small genres tied at a perfect 4.00 average and the winner was whichever the dictionary listed first (five Glam records "beat" forty Hard Rock ones). Under that sat a bigger error: **✓ albums were left out**, and most of the '60s–'80s were marked ✓ with only the ⭐s ever scored, so the '70s read as 99% ⭐ (129 of 130) when it's 46% (129 of 278). `AlbumAnalytics` now counts every *heard* album — a ✓ is a known "not a ⭐" — and ranks genres and decades by **⭐ rate** among those with 10+ heard (ties to more albums, then A–Z), so the order always matches the percentages printed beside it; averages are only taken across scored albums and shown as a detail. A Bayesian-weighted rate was tried and dropped for exactly that reason. Genres count an album under its Discogs *styles* ("Hard Rock"), not the broad genres everything shares ("Rock"), and "Folk, World, & Country" is no longer split into a phantom "& Country". The page lost LiveCharts' stock columns for hand-drawn bars in the app's palette (an ordinal gold ramp for ⭐→❌, gray for ✓), with hover detail, and is now three separate sections: **the 1001**, **albums you added** (the Replacements list against the 1001 — decade shares, most-added artists, artists new to the list; it has no genres or ratings to chart), and **the shortlist**. One `AlbumAnalytics.Report` feeds the page, its own PDF and — new — **two Insights pages at the end of the finished-list export** (`PdfExporter.Write`'s `appendix`). LiveCharts is gone from the build. |
| 2026-11-04 | **Exports open in a browser tab, and one Dock icon.** A finished PDF (Export page or Insights) now opens in a new tab of the default browser — asked of macOS each time, falling back to Preview — instead of a Finder window with the file selected; "Open again" and "Show in Finder" sit beside the last export (`ExportedFile`). The Dock was showing the app twice, pinned and again under recent apps: `--install` deleted the bundle and copied a new one, and a new folder is a new file to macOS, so the pinned icon stopped matching the running app. The install now empties and refills the same bundle with `ditto` (not `rsync`, which drops the extended attributes holding the non-Mach-O files' signatures) and re-registers it with Launch Services. Percentages everywhere go through `AlbumAnalytics.Percent`, which never rounds a non-zero share to "0%" or a near-total one to "100%" (the one ❌ in 775 read 0%). |
| 2026-11-04 | **Pre-push review sweep.** Fixes from a code review of the November work: Insights' load and PDF export catch errors instead of quitting the app; an album Music says is already in the library uses the tracks already there instead of waiting 60s for more; the shortlist pull and Keep moved off whole-word prefix matching onto `AppleMusicCatalog.SameRelease`, which now also tells "Weezer (Blue Album)" from "(Green Album)" (two different non-edition tags); `LoadAllListsAsync` falls back to the saved snapshot only when the sheet is unreachable (network, timeout, Google 5xx/429), so a layout or access problem shows its real error instead of passing as "Offline"; `AlbumGenres.FromArtist` splits with `SplitTags`, so "Folk, World, & Country" stays whole; the shortlist's background lookup saves year and genre in one write and looks up a missing genre once per session; Apple Music genre names are converted only for single-tag genres; and the hidden Rate page ignores keys. Still open: one shared throttle for Apple Music catalog calls. |

---

## Weak points (reviewed 2026-11-03)

Where the app can break, roughly most likely first, and what's been done about each.

- [x] **Tied to the source checkout** — settings, keys, the shortlist cache and exports lived in the
  project folder. *Fixed:* data in Application Support, exports in Documents, settings built in.
- [x] **Two Google sign-ins** — browser OAuth for the master list (its refresh token can expire after
  a week in a "testing" Google project) plus a service account for Potentials. *Fixed:* service account only.
- [x] **The shortlist stored twice, once in git** — `replacement-candidates.json` was tracked and
  always dirty. *Fixed:* untracked; local copy is an offline cache in Application Support.
- [x] **Rebuilds wiped macOS permissions** — unsigned app, new identity each build. *Fixed:* signed
  with the Apple Development certificate. (It's a free-account certificate: when it expires, Xcode
  issues a new one with the same name, and the grants should carry over.)
- [ ] **Apple Music add path** — the iTunes Search API (undocumented, ~20 requests/minute) and UI
  scripting Music's "Add to Library" button (breaks if Apple moves it; needs Music on the current
  desktop). No official alternative without a paid developer account; "Open in Music" is the fallback.
  Recheck after macOS/Music updates — `musictest` is the quickest way.
- [x] **Sheet layout assumed** — rating is written into column B by position. *Fixed:* the shape is
  checked on load (`RatingSession.LayoutProblem`); a shifted column refuses to open for rating.
- [x] **Nothing worked offline** — *Partly fixed:* Browse, Export (and an export) use the last good
  read, marked as offline. Rating still needs the sheet, by design: it writes to it.
- [ ] **Leftovers** — the retired iPhone head (code kept on purpose; its build output is ~1.3 GB of
  untracked files), and ~250 one-track albums in the favorites playlist from the old library-based
  pick. Neither breaks anything.

---

## 3b. Current status & what we're building

**Live workflow (the "why"):** Andrew is ~750/1001 through the list, listening 3–5 albums at a time.
As he goes he queues albums into Apple Music **PLAYLIST1** (from the 1001) and **PLAYLIST2**
(recommendations), listens, decides, then wants to remove them to keep the queue clean — cutting the
manual bookkeeping.

**Where we are right now:**
- ✅ App builds, signs, installs, and **runs on the iPhone 15 Pro** (interpreter mode).
- ✅ Apple Music: **adding** to the existing PLAYLIST1/PLAYLIST2 **works on-device** (confirmed).
- ✅ Desktop potentials sync (Google Sheets) works and is verified.
- ✅ **Mobile Sheets sync** — confirmed on-device: the Replacements tab reads live from Google Sheets on the phone.
- ✅ **Playlist UX (Phase 3)** — named failures + reason on push, a "delete these from Apple Music"
  checklist for add-only's blind spot, and track-count visibility for possibly half-deleted albums.
- ✅ **Phase 2 — all four features done:** rate (new REST client for the master sheet),
  add-to-shortlist (Discogs lookup), edit years, browse/search Must Hear + Replacements. Confirmed
  looking good on-device.
- ✅ **iOS app icon** — the home screen no longer shows a blank placeholder.
- ✅ **Visual polish pass.** Found a reliable way to screenshot `mobilepreview` after all (capture by
  exact window ID via Quartz's window list, not a screen-region guess — see the dev tool note below),
  combined with temporarily forcing the TabControl's `SelectedIndex` to see each of the 5 screens.
  Fixed: a clipped "Replacements" tab label (renamed to "Shortlist"), the Rate card stretching to
  fill the screen around mostly empty space, clipped Apple Music button labels on the Playlist tabs,
  a missing (and once added, invisible-due-to-z-order) empty-state message there, and a redundant
  sync-status line on the Shortlist tab.
- ✅ **Ratings are editable.** A Revisit queue (filterable to one symbol), a ✎ on every 1001 row in
  the browse window, and the phone Rate tab's "Jump to…" picker all lead to the same thing: re-rating
  an album that already has a rating. Dropping a ⭐ takes the album off Must Hear again.
- ✅ **The phone can reach any album from the Rate tab.** The search field is pinned in the header,
  above the keyboard rather than under it, and an A–Z button opens the whole 1001 with a draggable
  index rail for when skimming beats typing.
- ✅ **Mobile Rate has all three queues** (Next up · Backfill ✓ · Revisit) — shuffle is still Mac-only.
- ✅ **Lists show the rating you just gave**, on both apps, and the phone's 1001 tab now refreshes
  itself from the sheet instead of living on the snapshot it was built with.
- ✅ **The phone app has a design system** (`Views/Mobile/MobileTheme.axaml`) and a hand-built
  bottom nav, scoped to `MainView` so the Mac windows are untouched.
- ✅ **Years are the album's first release**, not a reissue's — see `PreferOriginalRelease`.
- ✅ **Apple Music favorites are backfilled from the list.** Every album on the finished list — the
  ⭐ picks plus the replacements — gets a heart in Apple Music if it hasn't got one, and is mirrored
  into the "Almost forgot to listen to these today" playlist. **Mac only** (see §5b), read-only
  rehearsal first via `favtest`, and a two-press confirmation on the Mac app's card.
- ✅ **Browse keeps your place and shows the year.** The mobile browse rows carry the album's year;
  the sort bar has a "#" column, which is the way back out of a sort; and scrolling away from where
  you actually are in the 1001 raises a "↩ Back to #756 Tical" chip that puts you back.
- ✅ **Playlist 2 rows reach both replacement lists.** Each row of the recommendations queue carries
  two verdicts on both apps: **＋ Shortlist** (write it down, decide later) and **Keep** (decided —
  on to the replacements list proper, and marked kept on the shortlist). They are different lists
  and both are worth having; `Operations.KeepFromPlaylistAsync` is the shared route, and it writes
  the replacements list *first* so a refusal there can't leave the shortlist claiming a decision
  that never happened.
- ✅ **The export says where the list comes from.** An opening page on the PDF: what the book is,
  what its panel ruled out, and the one rule Andrew has let go — a great EP counts.
- **The whole roadmap (Phases 1–4) is now done.**

**User stories (what Andrew wants):**
- *As I go through the 1001*, tap to add an album to my real Apple Music **PLAYLIST1** (and recs to **PLAYLIST2**).
- Keep a **clean working list** I can add **and remove** from. Apple Music can't remove, so the app owns "clean" and (planned) shows a **"delete these from Apple Music" checklist**.
- **Sync my potentials shortlist** across Mac ↔ phone.
- **Rate albums** as I listen, on the phone.
- **Add to the shortlist** on the go (with Discogs year lookup).
- **Browse/search** the 1001, Must Hear, and replacements lists on the phone.
- **Edit/enter years** on the phone.
- **One-click re-deploy** when the weekly trial lapses.
- Make it **look clean** (tidy layout, an app icon) — not necessarily a desktop clone.
- When something fails (e.g. an album not on Apple Music), **tell me which one and why**.

**Roadmap:**
1. ✅ **Phase 1 — Mobile Sheets sync** (foundation). Confirmed on-device. Unlocks persistence for everything below.
2. ✅ **Phase 2 — Four features:** rate · add-to-shortlist · browse/search all lists · edit years — all persisting via sync.
3. ✅ **Phase 3 — Playlist UX:** name failed adds + why; the "delete these from Apple Music" diff checklist; track-count visibility for partial (half-deleted) albums.
4. ✅ **Phase 4 — Clean visual pass + iOS app icon.** Both done.

**The roadmap is complete.** Future work is genuinely new ideas, not a backlog — see §8.

---

## 4. Getting the app on the iPhone (deploy)

Free Apple account = the app must be **re-deployed every 7 days** (the signing profile expires).
The $99/yr Apple Developer Program removes that and enables TestFlight ("install once").

### One-time setup (done)

1. **Xcode** installed (full app, not just Command Line Tools).
2. **Apple ID** added in Xcode → Settings → Accounts (creates a free "Personal Team").
   Team ID: **`FCSUH87SVL`**.
3. **Signing certificate** created: Xcode → Accounts → Manage Certificates → + → Apple Development.
   - *Gotcha we hit:* the cert showed `CSSMERR_TP_NOT_TRUSTED` because the **Apple WWDR G3
     intermediate** was missing. Fix: `curl -sO https://www.apple.com/certificateauthority/AppleWWDRCAG3.cer && security import AppleWWDRCAG3.cer -k ~/Library/Keychains/login.keychain-db`.
4. **Developer Mode** on the phone: Settings → Privacy & Security → Developer Mode → On → restart.
5. **Provisioning profile** (free accounts can't make these from the .NET CLI). We generate one with
   a throwaway Xcode project + `xcodebuild -allowProvisioningUpdates -allowProvisioningDeviceRegistration`
   (see `scratchpad/SignHelper` pattern), then copy it to `~/Library/MobileDevice/Provisioning Profiles/`.

### Build + install (repeat each deploy)

```bash
# Build & sign for the device. NOTE the flags — all are load-bearing (see §7):
dotnet build 1001AlbumHelper.iOS/1001AlbumHelper.iOS.csproj \
  -c Debug -f net10.0-ios -r ios-arm64 \
  -p:MtouchDebug=false -p:ValidateXcodeVersion=false \
  -p:MtouchInterpreter=all -p:UseInterpreter=true \
  -p:CodesignKey="Apple Development: alarkins93@yahoo.com (85U29FF3K6)"

# Install + launch (phone unlocked):
APP=1001AlbumHelper.iOS/bin/Debug/net10.0-ios/ios-arm64/1001AlbumHelper.iOS.app
xcrun devicectl device install app --device <UDID> "$APP"
xcrun devicectl device process launch --device <UDID> com.larkins.albumhelper
```

- Device UDID: **`00008130-001468C02630001C`** (Andrew's iPhone 15 Pro).
- First install of any build: trust the developer on the phone —
  Settings → General → VPN & Device Management → *Apple Development: alarkins93@…* → **Trust**.
  **This comes back whenever the profile is re-issued, not just on a genuinely first install** — if
  the 7-day profile has fully lapsed, the renewal mints a new certificate and the phone drops the
  old trust. The symptom is an install that succeeds followed by a launch that fails with
  `FBSOpenApplicationServiceErrorDomain error 1` / "invalid code signature, inadequate entitlements
  or its profile has not been explicitly trusted by the user". Nothing is wrong with the build —
  go and tap Trust. (Hit on 2026-08-19, after the profile expired on 08-12.)

---

## 5. Apple Music linking

Both apps drive the same two playlists, targeted **by name** — `PLAYLIST1` (from the 1001) and
`PLAYLIST2` (recommendations) — but they get there by completely different routes, because
MediaPlayer is iOS-only and AppleScript is Mac-only. `IApplePlaylistWriter` is the seam;
`AppleMusic.Writer` is set by each head at startup (iOS in `AppDelegate`, Mac in `Program.Main`),
and stays null on Windows/Linux, where the playlist UI simply shows its Apple Music buttons disabled.

**On the iPhone** (`MediaPlayerPlaylistWriter`):
- Apple's **MediaPlayer** framework (`MPMediaLibrary` / `MPMediaPlaylist`), authorized at
  runtime (the `NSAppleMusicUsageDescription` prompt). Needs an Apple Music subscription. **No paid
  MusicKit entitlement required.**
- Albums are matched to Apple Music store IDs via the **public iTunes Search API** (`AppleMusicCatalog`),
  then added by product ID.

**On the Mac** (`MusicAppPlaylistWriter`, in the Desktop head):
- Drives **Music.app over AppleScript**, run through `osascript`. Scripts are passed their arguments
  via `on run argv` — never string-interpolated — so an album title full of quotes can't break, or
  rewrite, the script.
- **The release is chosen in the catalog, never the library.** `AppleMusicCatalog.FindAlbumAsync`
  (iTunes Search API, falling back to the artist's full catalog) picks the **original, else a
  remaster, and a deluxe edition only when nothing leaner exists**; singles/EPs sharing the name and
  sequels that merely start the same way ("Tical 2000" for "Tical") never qualify, and the explicit
  version beats a bleeped one. Check a pick without writing anything:
  `dotnet run --project 1001AlbumHelper.Desktop -- lookup "Method Man|Tical"`.
- **The library is only a staging area.** `search library playlist 1` reaches the library alone,
  never the catalog, and AppleScript has no verb for adding a catalog album — so when the library
  doesn't already hold the whole release, the writer opens its page in Music (`music://` link),
  waits until the page on screen *is* that release, and presses Music's toolbar **"Add to Library"**
  over System Events UI scripting (it reads "Download" once added). It then polls the library until
  the release's tracks arrive and `duplicate`s exactly those — filed under the release's own name and
  artist — into the playlist. (Until 2026-11-03 the album was picked from library search hits with a
  fewest-tracks tie-break, so a single song of an album sitting in the library won, and one song went
  into the playlist. The favorites playlist still holds ~250 such short albums from that.)
- **Removing** deletes the album's tracks from the user playlist by persistent id — the playlist
  only, never the library. (Don't name an AppleScript variable `removed` inside `tell "Music"`; it's
  a dictionary term, and the script won't compile.)
- UI scripting needs **Accessibility** for the app (System Settings ▸ Privacy & Security ▸
  Accessibility), and can only see Music's window when it's **on the current desktop** — not a
  different Space, and not hidden behind a full-screen app. Both are reported in plain words when
  they're the problem, with an **Open in Music** link as the manual fallback.
- Reading a playlist back and folding its tracks into albums is shared: `PlaylistTracks.CollapseToAlbums`.
- Verify headlessly: `dotnet run --project 1001AlbumHelper.Desktop -- musictest` — creates its own
  scratch playlist, pushes real albums into it, reads them back, and deletes it, so it never touches
  PLAYLIST1/PLAYLIST2.

### The big constraint: add-only

Apple's MediaPlayer framework can **add** items to a playlist but has **no API to remove, clear, or
delete** — for any playlist. "Wipe and remake" doesn't work either (there's no wipe). The only
framework that can truly edit a playlist is **MusicKit**, which is Swift-only and unreachable from
C#/.NET.

**So the model is:** the app's Playlist tabs are your **clean working list** (full add/remove, and
— next — synced via Sheets); Apple Music is an **add-only listening queue**. You occasionally clear
the Apple Music playlist yourself; everything else is automated.

**The "delete these" checklist:** removing an album from a Playlist tab that was already pushed to
(or pulled down from) Apple Music doesn't just delete it locally — Apple Music still has it, and the
app can't remove it there. Instead it moves to a checklist ("Delete these from Apple Music (n)") at
the bottom of that tab, so nothing pushed there is ever silently forgotten; check it off once you've
deleted it yourself — or just pull down again, which ticks off anything that has actually gone.

**Pulling Playlist 2 also feeds the shortlist.** PLAYLIST2 is the recommendations queue, so an
album sitting in it that the shortlist has never heard of is a potential replacement nobody wrote
down. Every pull of Playlist 2 hands the **whole playlist** to `ShortlistIntake`, which puts anything
missing on the potentials shortlist as a pending candidate (with the edition furniture trimmed off
its title) and saves the list where that device saves it — the local JSON *and* the Potentials sheet
on the Mac, the sheet alone on the phone, which has no writable copy. Albums already ruled on are
left alone, dropped ones included; the status line says what went on.

It compares against the shortlist rather than against the pull's new arrivals, and that distinction
cost us a round trip to find: "new" means new to *this device's* working list, so a pull on the phone
left the Mac's next pull with nothing to offer and an album that had reached neither shortlist stayed
lost. Comparing against the shortlist itself is stateless — same answer on either device, and running
it twice changes nothing. Playlist 1 doesn't do this at all: it holds the 1001 itself, not candidates
to replace it.

**Pull down vs Push all are deliberately asymmetric.** *Pull down* is a full sync: the Apple Music
playlist wins, and the working list is rebuilt to match it (see `PlaylistStore.SyncFromAppleMusic`) —
so albums queued in the app but never pushed are dropped. *Push all* can only ever add, because
that's all MediaPlayer offers. The asymmetry is the add-only constraint showing through: Apple Music
is the listening queue and the app is the clean list, so it's the app that has to give way when the
two disagree. Reading a playlist back also carries each album's current track count, and a
low one (≤3) is flagged in the list as possibly half-deleted.

### 5b. Favorites — and the one thing only the Mac can do

The finished list and Apple Music's own favorites are the same list twice over, and they drift.
**Backfill favorites** reconciles them: every album on Must Hear + Replacements that Apple Music
hasn't got a heart on gets one, and the same albums are mirrored into the playlist
`Almost forgot to listen to these today` — a deliberate duplicate of the favorites, because a
playlist is somewhere you can sit down and play.

- **Mac only.** `album favorited` is a read/write property on a Music.app track over AppleScript.
  The iPhone's MediaPlayer framework has **no favorite property at all** — the same Swift-only
  MusicKit wall the missing remove API runs into (§5). `IApplePlaylistWriter.CanFavorite` is the
  seam; the phone reports it rather than half-doing the job.
- **The two halves are checked separately.** An album can be favorited but missing from the
  playlist — that is exactly what a hand-kept duplicate drifts into — so each gap is decided on its
  own. Both are read fresh from Apple Music every run, which makes the whole thing idempotent and
  safe to interrupt: it only ever turns a favorite *on*.
- **Reading is cheap, writing is not.** The favorites are read with three
  `whose album favorited is true` bulk gets — one AppleEvent each, ~2s against a 33k-track library,
  where walking the library and asking per track would be tens of thousands of round trips. But
  *setting* one costs a `search library playlist 1`, the same slow call an add costs, so the run is
  a second or two per album it has to touch. `MusicAppPlaylistWriter` memoizes the last search one
  term deep so favorite-then-add for the same album searches once, not twice.
- **Rehearse first:** `dotnet run --project 1001AlbumHelper.Desktop -- favtest` names what would be
  touched and writes nothing. Run it: `-- backfill-favorites`, or the Mac app's "Backfill favorites"
  card, which previews on the first press and only writes on the second.
- **Names have to be matched at both ends, and loosely.** The list and Apple Music disagree about
  album names *and* artist credits, and the backfill has to survive both — once when it goes looking
  for an album, and again when it checks whether it already has it.
  - *Titles:* the list writes "Audioslave (self titled)", "Tarzan (soundtrack)", "Metallica (Black
    Album)"; Music has never heard of any of them, and stocks its own furniture besides — "Led
    Zeppelin (Remastered)", "The Clash (2013 Remastered)". So the search retries on the bare name,
    and the fallback compares **bare against bare, by equality** — a prefix would let a search for
    "Led Zeppelin" return "Led Zeppelin II" for the self-titled debut.
  - *Artists:* "Ozzy Osborne", "Simon And Garfunkel", "Dinosaur" for Dinosaur Jr., "Chicago Transit
    Authority" where Music says "Chicago". `DiscogsApiClient.ArtistsOverlap` bridges these, which is
    the leniency `AppleMusicCatalog.FindBestMatch` already applied at the far end.
  - **The check matters as much as the search.** While `Operations.Holds` used the strict rule, an
    album the backfill had just favorited read as *still missing* next run, so every run redid the
    same work and pushed the same albums into the playlist again — and Apple Music has no remove.
    Three runs on 2026-08-25 left **471 duplicate track rows across 32 albums** that way. If the
    plan's "to touch" count doesn't fall to roughly the count of genuinely-missing albums after a
    run, that bug is back.
- **Runs on 2026-08-25** — 536 albums on the lists, starting from 531 favorited / 255 in the
  playlist. Run 1 (exact names only): 81 favorited, 321 added, 76 albums not found. Run 2 (bare
  search term): 30 more found. Run 3 (bare-on-both-sides match): 19 more. Settled at **507 of 536
  favorited**, with 33 still out of reach — 27 that Music genuinely can't find and 6 the check
  can't recognize, all of them sheet typos: "Buckhead" for Buckethead, "Mastadon", "Merhuggah",
  "Perserverance", "Favorite Worth Nightmare", `Rocks"`, "Untitled" for Led Zeppelin IV. Fixing
  those is a data job on the sheet, not a matching problem — going further would mean fuzzy matching,
  and a wrong add can't be taken back.

---

## 6. Updating the potentials list (Google Sheets sync)

Keeps the potentials shortlist in sync across devices via a **"Potentials" tab** in the Google Sheet.

- Auth is a **Google service account** (no login screen), and since 2026-11-03 it's the *only* Google
  auth: the desktop's master-list client (`GoogleSheetsWriter`) uses the same key through Google.Apis
  instead of the browser OAuth it used to. Key file: `1001AlbumHelper/service-account.json`
  (git-ignored, embedded into the app at build). Share the sheet with the service account email as **Editor**.
  - Service account: `albums-sync@secret-319922.iam.gserviceaccount.com`.
- Config lives in `appsettings.json` → `GoogleSheets:{PotentialsTab, ServiceAccountKeyFile}`.
- Code: `CandidateSheet` talks to the **Sheets v4 REST API directly** (service-account JWT signed with
  RSA → access token → HTTP). It deliberately avoids the **Google.Apis** client library, which can't
  be made to work on iOS (trimmed → `TypeInitializationException`; un-trimmed → the app is big enough
  that the runtime crashes at launch). Only `System.Text.Json` + `RSA` + `HttpClient`.
  `CandidateRepository` picks Sheets-when-configured with local JSON as an offline cache; on the phone
  it loads the config + key from **embedded resources** (baked into the assembly — the phone has no
  data folder). First sync **seeds** the sheet from local so an empty sheet never wipes it.
- Verify / seed headlessly: `dotnet run --project 1001AlbumHelper.Desktop -- synctest`.
- Rehearse what a Playlist 2 pull would add to the shortlist, writing nothing anywhere:
  `dotnet run --project 1001AlbumHelper.Desktop -- intaketest`.

**Status:** working + verified on both desktop and the phone (Replacements tab reads live).

**The master list ("1001 albums") needs the same REST treatment, separately.** `CandidateSheet` only
covers the Potentials tab; rating and Must Hear insertion go through `NumberedList`/`RatingSession`,
which talk to an `ISheetsClient` — `GoogleSheetsWriter` (service account, Google.Apis) on desktop, `RestSheetsClient`
(service account, same REST approach as `CandidateSheet`) on mobile. `RestSheetsClient` is the harder
of the two: `InsertRowAsync` needs a hand-built `batchUpdate` (insertDimension + copyPaste) to match
`GoogleSheetsWriter`'s row-insert-with-formatting behavior exactly, since that's what a ⭐ rating's
Must-Hear-list insert depends on. Verify / re-verify headlessly: `dotnet run --project
1001AlbumHelper.Desktop -- resttest` — exercises every `ISheetsClient` operation against a scratch
tab it creates and deletes, so it never risks the real lists.

---

## 7. Known issues & gotchas

- **iOS runtime version mismatch.** The phone runs **iOS 26.5.2**, but the newest .NET-for-iOS pack
  for our SDK is **26.4** (mono runtime 10.0.8). The AOT-compiled runtime crashes at launch on 26.5
  (`mono_jit_init → load_aot_module → abort`). **Workaround: build with `-p:MtouchInterpreter=all`**
  (interpreter mode skips AOT-module loading). Slower at runtime, but it runs. Revisit when a newer
  .NET SDK ships a 26.5+ iOS pack, then drop the interpreter flag + `ValidateXcodeVersion=false`.
- **Simulator is unreliable** on this toolchain (same crash, intermittent). Don't trust it to verify
  — use the device, or a matched workload.
- **Build flags that must be passed** (the SDK overrides them if set in the .csproj):
  `MtouchDebug=false` (or the debug agent SIGABRTs with no debugger), `ValidateXcodeVersion=false`
  (Xcode 26.6 vs pack 26.4), `MtouchInterpreter=all` (above).
- **Google.Apis is unusable on iOS.** Trimmed, its static initializers throw
  `TypeInitializationException`; un-trimmed, the app is too big and the runtime crashes at launch. The
  potentials sync therefore uses the **REST API directly** (see §6). Google.Apis remains only on the
  desktop-only list-writer.
- **iOS sandbox + file walks.** `Directory.EnumerateFiles` on a parent directory throws "Operation
  not permitted" outside the sandbox — any such walk on iOS must catch `IOException` /
  `UnauthorizedAccessException` (see `Operations.ResolveDataDir`). This silently killed sync via a
  type-initializer crash before it was fixed.
- **iOS app icon setup.** A single-size (1024×1024) `Assets.xcassets/AppIcon.appiconset` — the
  modern Xcode 14+ convention, no need to hand-generate every legacy size. Two things it needed that
  a normal Xcode project handles for you: the `.csproj` must set `<AppIcon>AppIcon</AppIcon>` (the
  MSBuild property `actool` reads — nothing wires it up automatically), and the source art must be
  fully **opaque** (no alpha channel) — the Mac icon's `.icns` bakes in transparent rounded corners,
  which iOS's own asset compiler rejects; it needs cropping past that rounding into a flat full-bleed
  square first (iOS applies its own corner mask at render time).
- **Re-deploying:** double-click **`Re-deploy to iPhone.command`** (repo root) — it renews the 7-day
  profile via `xcodebuild -allowProvisioningUpdates`, rebuilds, and reinstalls. The signing stub lives
  in `1001AlbumHelper.iOS/SignHelper/`.
- **Never write the potentials sheet wholesale.** Two screens hold the shortlist at once and
  neither hears about the other's changes, so a plain "save my list" deletes whatever arrived
  meanwhile — this is how a Playlist 2 pull's new candidates vanished on 2026-08-21. Writes go
  through `CandidateRepository.PushAsync`, which reads the sheet and merges. It's safe because the
  app has no delete: if a candidate ever does need removing, it has to be done on the sheet by hand,
  and a device still holding the old row will put it back.
- **The iTunes Search API is rate-limited** (roughly 20 calls a minute, unauthenticated). One call
  per album on a Rate card is comfortably inside that; a *list* of thumbnails (the 1001 list, a
  playlist) is not, and would start coming back empty. That's why cover art lives on the one-album-
  at-a-time screens only. `AlbumArtwork`'s disk cache (in the app's writable folder, alongside the
  playlist JSON) means each album costs a lookup once, ever — so lists become feasible later mainly
  for albums already seen.
- **Apple Music removal** is impossible *on iOS* (see §5). Note that **Music.app on the Mac
  *can* delete** — its AppleScript dictionary has a working `delete` for playlist tracks. The app
  doesn't use it yet: the "delete these from Apple Music" checklist is still the shared model on
  both platforms. See §8.
- **Music.app's AppleEvents are slow.** Against a large cloud library (~33k tracks here) even a
  bare `count` can blow past AppleScript's default 60-second limit and fail with `-1712`. Every
  script in `MusicAppPlaylistWriter` therefore wraps itself in `with timeout of 280 seconds`, and
  the `osascript` process gets a 5-minute ceiling of its own. If a Mac push ever starts timing out,
  that pair of numbers is what to raise.
- **Cloud status is the only thing separating two listings of one album.** Where Apple Music
  stocks an album twice, the two can be identical on every field the AppleScript dictionary
  exposes — name, artist, track numbers, release date. Don't try to pick the better *edition*;
  there's nothing to pick on. Choose per song, on `cloud status`, which is what
  `PlaylistTracks.PreferPlayable` does. Statuses other than "no longer available" (subscription,
  purchased, matched, uploaded…) all count as playable — an unfamiliar one is far likelier to play
  than not.
- **`search library playlist 1` only finds albums already in the library** — never the catalog,
  however exact the name. That's why a Mac push adds the release to the library first (see §5).
- **UI scripting Music: windows on another Space are invisible.** System Events reports zero
  windows for Music when it sits on a different desktop than the frontmost app (a full-screen
  terminal counts), and `activate` from a background process won't switch to it. Keep Music on the
  same desktop. Address its page by path (`toolbar 1`, the content `scroll area`), not
  `entire contents`, which takes ~15s.
- **Never send ⌘-digit keystrokes to the app to drive it.** The Rate page rates on bare 1–4; it now
  ignores keys with modifiers and the window takes ⌘1–⌘5 first, but on 2026-11-03 a scripted ⌘2–⌘4
  wrote three real ratings (reverted). To look at a section, launch with
  `ALBUMHELPER_START_SECTION=Browse` (Rate, Browse, Shortlist, Playlists, Export, Insights, Tools) instead.
- **Data lives in `~/Library/Application Support/1001AlbumHelper`** for `dotnet run` and the
  installed app alike — no launch-profile tricks needed. `ALBUMHELPER_DATA_DIR` overrides it.
- **Rebuilds and macOS permissions.** The build signs with the Apple Development certificate (the
  free account's), whose designated requirement — bundle id + certificate name — is the same build to
  build, so Accessibility and Automation grants stick. An ad-hoc signature changes every build, and
  macOS quietly drops the grants. The executable is the apphost itself in `Contents/MacOS`, not a
  launcher script, so the bundle's signature is the one macOS checks.
- **Automation permission.** The first Mac push makes macOS ask to let the app control Music
  (System Settings ▸ Privacy & Security ▸ Automation). Denying it makes every push fail; there's no
  way to re-prompt, it has to be re-enabled there by hand.
- **`devicectl install`/`launch` are flaky over the tunnel connection.** "Connection reset by peer"
  on install usually succeeds on a plain retry. "Launch failed... Locked" on `process launch` means
  exactly that — unlock the phone (Face ID/passcode) and either retry the launch command or just tap
  the app icon; it is not a build problem.
- **⚠ Tests can reach the live spreadsheet. Never build a `CandidateRepository` or a
  `DiscogsApiClient` in a test.** `appsettings.json` and `service-account.json` are **embedded
  resources of the shared assembly** (see its .csproj), and `EmbeddedConfig` falls back to them when
  no file is found. The test project references that assembly, so `CandidateRepository.Create()`
  inside a test comes back **sync-enabled against the real Potentials tab** no matter what
  `Operations.ProjectDir` resolves to — and `PushAsync` then writes to it. This is not theoretical:
  on 2026-08-25 a run of six new tests for `KeepFromPlaylistAsync` flipped five real candidates to
  "kept" on the live sheet (repaired by pushing this Mac's `replacement-candidates.json` back over
  it, which `ReplacementCandidates.Merge` makes exact — local status wins per album, sheet-only rows
  survive). The same fallback means a Discogs token is live in tests too, so a "no year" path will
  quietly go and *find* one over the network.
  **Test the pieces that take an `ISheetsClient`** — `AddReplacementAlbumAsync`, `NumberedList`,
  `RatingSession` — against `FakeSheets`, and leave the orchestration that builds a repository of
  its own to be checked by hand in the app. A giveaway: a test suite that suddenly takes ten seconds
  instead of a fifth of one is out on the network.
- **Screenshotting `mobilepreview` on the Mac reliably.** Don't query the window's position via
  `osascript`/System Events and `screencapture -R` that region — if the window isn't on the currently
  active Space (common for a window from a freshly-launched background process), that captures
  whatever else is actually on-screen there instead (once grabbed an unrelated Safari window).
  Capture by **window ID** instead — reliable regardless of Space:
  `Quartz.CGWindowListCopyWindowInfo` (via a scratch venv with `pyobjc-framework-Quartz`) to find the
  window number for owner "1001AlbumHelper", then `screencapture -l<id> -o out.png`.
  Watch the owner name: run from `dotnet run` it is `1001AlbumHelper`, but the **installed .app
  bundle reports `1001 Albums Helper`** (with spaces) — filtering for the wrong one looks exactly
  like "the app didn't start". `screencapture -l` also needs Screen Recording permission for
  whichever terminal is running it, or it fails with "could not create image from window". To see a
  specific tab, set **`ALBUMHELPER_PREVIEW_TAB`** (0-4: List, Rate, Shortlist, Playlist 1,
  Playlist 2) — there is no need to edit `MainView.axaml`, which is what this note used to say.
  Note also that **synthetic input doesn't reach the window** without Accessibility permission for
  the terminal: `CGEventPost` scroll/keystrokes are silently swallowed, and osascript System Events
  keystrokes go nowhere. To see a state that only appears after scrolling, drive it from code —
  a temporary `Rows.ScrollIntoView(...)` on a delay, logging the state to stdout, then revert.

---

## 8. Future features & ideas

- **Shuffle on mobile Rate** — the desktop rating window offers it; the phone now has all three
  queues but still walks each in list order.
- **Cover art elsewhere** — the browse lists (1001 / Must Hear / Shortlist) and the playlist tabs
  are the obvious next places, but they'd need a way around the iTunes rate limit (§7): load only
  the rows actually on screen, keep a real on-disk index rather than one-file-per-album, and accept
  that a first scroll through unseen albums will be patchy. Worth doing for the playlist tabs first
  — they're short lists of albums that have usually been looked up already.
- **Real removal on the Mac.** Now that the Mac talks to Music.app, the one thing iOS fundamentally
  can't do is available here: Music's AppleScript `delete` really does remove tracks from a playlist.
  The "delete these from Apple Music" checklist could become an actual button on the Mac — tick it and
  the app does the deletion — while the phone keeps the manual checklist. Worth doing; it's the single
  biggest bit of the manual bookkeeping left. (It also slightly weakens the "rewrite in Swift for
  MusicKit" argument below, at least for the desktop half of the workflow.)
- **Weekly re-deploy pain** — mostly solved 2026-07-29 with an in-app "Renew iPhone trial" button on
  the Mac app (§3 table); still 7-day manual either way unless we spring for the $99 Apple Developer
  Program + TestFlight (install once, no expiry).
- **Rate view: show current position, not just single-card.** Andrew wants the Rate screen to open
  already scrolled/landed on his actual progress — including seeing ratings already given, not just
  the bare next-unrated card. `RatingSession.Rebuild` (in `Data/RatingSession.cs`) already computes
  "first unrated" correctly (filters to blank-rating rows, sorts by `SheetRow`, resets index to 0), so
  `Current` is always the right album on open. What's missing: both `RateView` (mobile) and
  `RatePage` (desktop) are single-card UIs with no visible list/context around that position — if
  what's wanted is an actual scrollable list (like `AlbumListView`) that highlights/scrolls to the
  first unrated row while showing neighboring rows and their ratings, that's new UI, not yet built.
  Needs clarifying with Andrew before implementing further.

### "Maybe rewrite away from C#?" — honest take

The friction we've hit is almost entirely **iOS-toolchain**: the runtime version mismatch, AOT +
signing dance, and — crucially — **no access to MusicKit** (which is what would let us *remove* from
Apple Music playlists). Options, with trade-offs:

| Direction | Wins | Costs |
|---|---|---|
| **Native Swift / SwiftUI (iOS only)** | Full **MusicKit** → real playlist editing incl. **remove**; no runtime/AOT/signing pain; best iOS feel. | A separate codebase from the Mac app; rewrite the mobile UI in Swift; maintain two apps. **This is the one that solves the remove problem.** |
| **Flutter / React Native** | One cross-platform mobile codebase; big ecosystems. | Still need native bridges for MusicKit; another full rewrite; Dart/JS instead of C#. |
| **Web app (Blazor / React PWA)** | One UI everywhere via the browser; trivial deploy. | **No Apple Music library access at all** from the web; needs hosting. |
| **Stay on Avalonia/.NET** | Keep the shared Mac+iPhone codebase and the existing engine. | Live with the interpreter workaround + no MusicKit (no remove). |

**Bottom line:** if editing Apple Music playlists (removing after listening) becomes important, the
compelling move is a **native Swift iOS app** — that's the only path that unlocks MusicKit. The Mac
app can happily stay Avalonia/.NET. Everything else is a lateral move.
