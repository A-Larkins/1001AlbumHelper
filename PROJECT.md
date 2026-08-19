# 1001 Albums Helper — Project Handbook

A living reference for the app: what it is, how it's built, how to ship it, and where it's going.
**Update this as we go.** Last updated: 2026-08-19 (Apple Music playlists on the Mac).

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
│   │                           PlaylistStore, Operations, RatingSession, AlbumProcessor
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
│                               MusicAppDiagnostic (its `musictest` check)
├── 1001AlbumHelper.iOS/        → iPhone head (bundle id: com.larkins.albumhelper)
└── 1001AlbumHelper.Tests/      → xUnit tests (110 as of this writing)
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
| 2026-08-04 | **Two bug fixes, both apps.** (1) Browsing the **Replacements** list offered "＋ P1", so recommendations queued into PLAYLIST1; the ＋ button now follows the list you're in (1001/Must Hear → P1, Replacements → P2) on both `AlbumListView` and `ListViewerWindow`. The label and pressed-state moved onto `ViewRow` itself, since these lists virtualize and a label set directly on the button rode its recycled container onto other rows. (2) **Mobile had no way to keep or drop a candidate** — `ReplacementsView` gained Keep/Nah/Undo matching `CandidatesWindow`, and now hides decided rows like desktop does. Keep writes to the *master* spreadsheet, which the phone couldn't reach: `Operations.AddReplacementAlbumAsync` gained an `ISheetsClient` overload, and the config-and-key dance `AlbumListView` did inline became `MobileSheets`. |
| 2026-08-14 | **Cover art on the Rate screens** (Mac *and* iPhone). The album card now shows the sleeve, fetched through the same `AppleMusicCatalog` lookup the playlist export uses — so the art shown is the art of the album we'd actually add to a playlist. New `AlbumArtwork` caches decoded covers in memory and the downloaded JPEGs on disk, and warms the *next* album's art while you're rating the current one, so it's usually already there. A "♫" sleeve placeholder holds the space while a lookup runs (and for albums with no art), so the card never jumps between two heights. The desktop window grew 600→680 tall to fit it. |
| 2026-08-19 | **Apple Music playlists on the Mac.** The Mac could queue albums into Playlist 1/2 but had nowhere to see them and no way to push them — `AppleMusic.Writer` was set only by the iOS head. Added `MusicAppPlaylistWriter`, which drives **Music.app over AppleScript** (`osascript`), and a `PlaylistWindow` with the phone's Import / Push all / remove / "delete these" checklist. Where iOS resolves a store id via the iTunes Search API and adds by id, the Mac asks Music to `search` and duplicates the resulting tracks — and with Sync Library on that search reaches the **whole Apple Music catalog**, so obscure 1001 entries resolve just like on the phone, with no iTunes rate limit in play. Album choice runs through the very same `AppleMusicCatalog.FindBestMatch` the phone uses, so both apps prefer the original release over a "(Deluxe Edition)" identically. Track→album folding moved into a shared `PlaylistTracks` both writers use. Verified end-to-end by a new self-cleaning `musictest` diagnostic (scratch playlist, created and deleted, never touches PLAYLIST1/2). |
| 2026-08-19 | **Import became a pull-down sync** (both apps). It used to *merge* — add what Apple Music had, never remove — so the working list only ever grew. Now the Apple Music playlist is the source of truth: after a pull the list holds exactly what that playlist holds, and anything queued locally but not in Apple Music is dropped. Two things deliberately survive: albums on the **"delete these" checklist stay on it** rather than being resurrected as active (Apple Music still having them is the whole reason they're listed — a merge-back would undo the user's removal on every sync), and a known album's **year is carried across**, since Apple Music's read-back has no year and our lists do. The nice inverse: a checklist album that's *gone* from Apple Music means the user has now deleted it by hand, so the sync ticks it off. Matching had to loosen to `DiscogsApiClient.TitlesLineUp` — with strict matching the catalog's "Tago Mago (2011 Remastered)" read as a different record from our "Tago Mago", so every sync would drop ours, re-add theirs, and lose the year. Buttons relabelled Import → **Pull down**. |
| 2026-07-29 | **Mac app parity pass.** Added a "Renew iPhone trial" button to `MainWindow` (shells out to `deploy-to-device.sh`, streaming its output into the activity log — see §4/§7) and "+P1"/"+P2" playlist buttons to `ListViewerWindow` (browse) and `CandidatesWindow` (potential replacements), matching the mobile app's `AlbumListView`/`ReplacementsView`. Both apps rebuilt, reinstalled, and committed (`f852195`). |

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
- Adding is *search-then-duplicate*: ask Music to `search library playlist 1 … only albums`, pick the
  right album, then `duplicate` its tracks into the target playlist. **With Sync Library on, that
  search covers the whole Apple Music catalog**, not just what's already been added — obscure 1001
  entries (Pere Ubu's *The Modern Dance*, Can's *Tago Mago*) resolve fine. It needs no iTunes Search
  API call at all, so the rate limit in §7 doesn't apply to a Mac push.
- Which album to add is decided by the **same `AppleMusicCatalog.FindBestMatch` the phone uses**
  (library hits are grouped into albums and handed to it), so both apps prefer the original release
  over a padded "(Deluxe Edition)" sitting beside it in exactly the same way.
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

**Pull down vs Push all are deliberately asymmetric.** *Pull down* is a full sync: the Apple Music
playlist wins, and the working list is rebuilt to match it (see `PlaylistStore.SyncFromAppleMusic`) —
so albums queued in the app but never pushed are dropped. *Push all* can only ever add, because
that's all MediaPlayer offers. The asymmetry is the add-only constraint showing through: Apple Music
is the listening queue and the app is the clean list, so it's the app that has to give way when the
two disagree. Reading a playlist back also carries each album's current track count, and a
low one (≤3) is flagged in the list as possibly half-deleted.

---

## 6. Updating the potentials list (Google Sheets sync)

Keeps the potentials shortlist in sync across devices via a **"Potentials" tab** in the Google Sheet.

- Auth is a **Google service account** (works on Mac *and* iPhone with no login screen — unlike the
  browser OAuth the desktop list-writer uses). Key file: `1001AlbumHelper/service-account.json`
  (git-ignored). Share the sheet with the service account email as **Editor**.
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

**Status:** working + verified on both desktop and the phone (Replacements tab reads live).

**The master list ("1001 albums") needs the same REST treatment, separately.** `CandidateSheet` only
covers the Potentials tab; rating and Must Hear insertion go through `NumberedList`/`RatingSession`,
which talk to an `ISheetsClient` — `GoogleSheetsWriter` (OAuth) on desktop, `RestSheetsClient`
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
- **A Mac push needs Sync Library on** in Music ▸ Settings. That's what makes
  `search library playlist 1` reach the whole Apple Music catalog instead of only local files —
  without it, anything not already downloaded simply won't be found, and every album fails with
  "Not found in Music".
- **Automation permission.** The first Mac push makes macOS ask to let the app control Music
  (System Settings ▸ Privacy & Security ▸ Automation). Denying it makes every push fail; there's no
  way to re-prompt, it has to be re-enabled there by hand.
- **`devicectl install`/`launch` are flaky over the tunnel connection.** "Connection reset by peer"
  on install usually succeeds on a plain retry. "Launch failed... Locked" on `process launch` means
  exactly that — unlock the phone (Face ID/passcode) and either retry the launch command or just tap
  the app icon; it is not a build problem.
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
  specific tab without fighting UI-click automation, temporarily set the TabControl's
  `SelectedIndex` in `MainView.axaml`, rebuild, relaunch, capture, repeat — then revert it.

---

## 8. Future features & ideas

- **Backfill mode + shuffle on mobile Rate** — the desktop rating window offers both; mobile's
  RateView only exposes the "not yet listened" queue so far.
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
  `RatingWindow` (desktop) are single-card UIs with no visible list/context around that position — if
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
