# 1001 Albums Helper — Project Handbook

A living reference for the app: what it is, how it's built, how to ship it, and where it's going.
**Update this as we go.** Last updated: 2026-07-29.

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
│   │                           CandidateRepository, AppleMusicCatalog, SyncDiagnostic,
│   │                           ISheetsClient (+ RestSheetsClient, GoogleServiceAccountAuth,
│   │                           RestSheetsClientDiagnostic) — the REST path for mobile writes
│   ├── Export/                 CsvGenerator, ExcelGenerator, PdfExporter
│   ├── Views/Desktop/          the six desktop windows
│   ├── Views/Mobile/           MainView + AlbumListView, RateView, ReplacementsView, PlaylistView
│   ├── Console/                ConsoleMenu (text menu; `dotnet run -- console`)
│   └── input/ output/ appsettings.json  (data + config; git-ignored where secret)
│
├── 1001AlbumHelper.Desktop/    → Mac/Win/Linux head (produces the "1001AlbumHelper" executable)
├── 1001AlbumHelper.iOS/        → iPhone head (bundle id: com.larkins.albumhelper)
└── 1001AlbumHelper.Tests/      → xUnit tests (70 as of this writing)
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
| Apple Music (MediaPlayer) | iPhone | Add albums to playlists. See §6. |
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
| 2026-07-29 | **Visual polish pass** across all 5 mobile screens (see §7 for the mobilepreview screenshot recipe that made this possible). **Roadmap (Phases 1–4) complete.** Opened **PR #1** (`iphone-support` → `main`) and pushed a public **README**. |
| 2026-07-29 | **Bug fix:** Apple Music catalog lookup missed real albums Apple's own `/search` buries in its relevance ranking (reproduced live: zero results for "Nine Inch Nails The Downward Spiral"). Falls back to the artist's full catalog via `/lookup` when `/search` comes up empty. |
| 2026-07-29 | **Real Apple Music removal, from the Mac** (branch `apple-music-removal`, off `iphone-support`) — `MusicAppPlaylistWriter` drives Music.app via AppleScript, which (unlike the phone's MediaPlayer API) can actually delete playlist tracks. New desktop window to browse + remove. **Not yet verified on-device** — blocked on a one-time macOS Automation permission dialog only a human can click through. |

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
- **The whole roadmap (Phases 1–4) is done.** Opened as **PR #1**: `iphone-support` → `main`
  (https://github.com/A-Larkins/1001AlbumHelper/pull/1), pushed to origin. Also fixed a real bug found
  by testing: Apple Music catalog lookup missed albums Apple's own `/search` buries in its relevance
  ranking — see `AppleMusicCatalog.FindAlbumAsync`'s fallback via `/lookup`.
- 🔄 **In progress: PR #2** (`apple-music-removal` → `iphone-support`, stacked on #1 —
  https://github.com/A-Larkins/1001AlbumHelper/pull/2): real Apple Music playlist removal from the
  Mac via AppleScript — see §5's new subsection. Code complete, builds clean, tests pass, **but not
  yet actually run against Music.app** — the first `osascript` call hit a one-time macOS permission
  dialog that needs a human's click, which this session couldn't get past. **Next step:** run the
  app, open "Apple Music playlists" from the main window, click through the permission prompt, and
  verify read + remove both actually work against a real playlist. The PR's test plan checklist
  tracks this.

**Git workflow:** repo is public — `github.com/A-Larkins/1001AlbumHelper`. Using feature branches +
PRs into `main` now (this was the first PR). Tried requesting a GitHub Copilot review via `gh`/API —
it doesn't recognize "copilot" as a requestable reviewer login for this repo, so that has to be done
(if available at all) from the PR's own "Reviewers" dropdown in the GitHub UI, not automatable here.

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

---

## 5. Apple Music linking

- Uses Apple's **MediaPlayer** framework (`MPMediaLibrary` / `MPMediaPlaylist`), authorized at
  runtime (the `NSAppleMusicUsageDescription` prompt). Needs an Apple Music subscription. **No paid
  MusicKit entitlement required.**
- Albums are matched to Apple Music store IDs via the **public iTunes Search API** (`AppleMusicCatalog`),
  then added by product ID.
- Targets your existing playlists **by name**: `PLAYLIST1` (from the 1001) and `PLAYLIST2`
  (recommendations).

### The big constraint: add-only **on the phone**

Apple's MediaPlayer framework (what iOS apps get) can **add** items to a playlist but has **no API to
remove, clear, or delete** — for any playlist. "Wipe and remake" doesn't work either (there's no
wipe). The only framework that can truly edit a playlist on-device is **MusicKit**, which is
Swift-only and unreachable from C#/.NET.

**So the phone-side model is:** the app's Playlist tabs are your **clean working list** (full
add/remove, synced via Sheets); Apple Music is an **add-only listening queue** there.

**The "delete these" checklist:** removing an album from a Playlist tab that was already pushed to
(or imported from) Apple Music doesn't just delete it locally — Apple Music still has it, and the
phone can't remove it there. Instead it moves to a checklist ("Delete these from Apple Music (n)") at
the bottom of that tab, so nothing pushed there is ever silently forgotten; check it off once you've
deleted it yourself. Reading a playlist back also carries each album's current track count, and a
low one (≤3) is flagged in the list as possibly half-deleted.

### The workaround: remove for real, from the Mac (branch `apple-music-removal`)

Music.app's **AppleScript** dictionary — unlike the phone's MediaPlayer API — can actually delete
tracks from a playlist. If the Mac and phone share an Apple Music library (iCloud Music Library /
Sync Library on), a removal made on the Mac syncs to the phone too (usually within minutes). Removing
a track from a *playlist* only unlists it there — it stays in the library untouched, so a wrong click
is cheap to undo.

- `MusicAppPlaylistWriter` (`1001AlbumHelper/Integrations/`, macOS-only — `IsAvailable` checks
  `OperatingSystem.IsMacOS()`): shells out to `osascript`, reading a playlist's tracks as plain
  `"album<TAB>artist"` lines rather than parsing AppleScript's own list-literal syntax back out (far
  more robust), collapsed to per-album entries with track counts — same shape the iOS reader uses.
  `RemoveAlbumAsync` runs `delete (every track of playlist X whose album is Y and artist is Z)`.
- New desktop window, **Apple Music Playlists** (button on the main window): pick PLAYLIST1/PLAYLIST2
  (or type another name), see what's really in it, remove albums one at a time.
- **Not yet verified end-to-end.** The first `osascript` call against Music.app blocks on a one-time
  macOS **Automation permission dialog** ("Terminal/[app] wants to control Music") that needs a
  physical click — an agent session can't get past this. Builds clean, all 72 tests pass, but a real
  human needs to click through it once (either run the app and click "OK" when prompted, or
  pre-approve under **System Settings → Privacy & Security → Automation**), then verify: read a
  playlist's contents matches reality, and a removal actually disappears from Music.app (and, given a
  few minutes, the phone).

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
- **Apple Music removal** is impossible (see §5).
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
  window number for owner "1001AlbumHelper", then `screencapture -l<id> -o out.png`. To see a
  specific tab without fighting UI-click automation, temporarily set the TabControl's
  `SelectedIndex` in `MainView.axaml`, rebuild, relaunch, capture, repeat — then revert it.
- **First AppleScript call to Music.app needs a one-time human click.** `MusicAppPlaylistWriter`
  (Mac-only removal, §5) shells out to `osascript`; the very first time a given controlling process
  automates Music.app, macOS blocks on an Automation permission dialog ("X wants to control Music")
  until someone clicks it — there's no way to script past this, and a headless/agent session will
  just hang (`osascript` sits there; kill it rather than waiting). Run the app yourself once, click
  "OK," and it's remembered from then on (revocable under System Settings → Privacy & Security →
  Automation).

---

## 8. Future features & ideas

- **Backfill mode + shuffle on mobile Rate** — the desktop rating window offers both; mobile's
  RateView only exposes the "not yet listened" queue so far.
- **Weekly re-deploy pain** — a small script, or spring for the $99 Apple Developer Program +
  TestFlight (install once, no 7-day expiry).

### Phase 5 — Mobile UI/UX polish v2 (branch `mobile-ui-polish`, off `iphone-support`, 2026-07-29)

Andrew looked at the app on-device again and called out a second round of polish, distinct from the
Phase 4 pass (which fixed clipping/z-order bugs, not layout/nav):

1. **Too much empty black space** — several screens (Rate card especially) have dead vertical space;
   tighten layout so it reads as designed, not stretched.
2. **Navigation:** replace the bottom tab bar with a **dropdown/picker** for switching between
   screens (decided over redesigning tabs in place — frees vertical space, and 5 destinations is a
   lot for a bottom bar).
3. **Rename tabs to match the Google Sheet's own naming**, since the phone is meant to mirror it:
   - "List" → **"Lists"**.
   - "Shortlist" → **"Potentials"** (the actual tab name in the sheet).
   - Rate, Playlist 1, Playlist 2 stay as-is — confirmed good.
4. **1001 list view:** make it obvious at a glance which albums are already rated/listened (not just
   browsable data), and have the list **default-scroll to the first not-yet-listened album** instead
   of always starting at #1.
5. **Bring ratings into the main 1001 list** — currently ratings only show in Rate/Must-Hear
   contexts; surface the ⭐/👍/👎/❌ per-row in the 1001 list itself.
6. **PDF export exists on desktop but not mobile.** Decided approach: a **flag-file trigger** —
   phone writes a "please export" request into the synced Google Sheet; next time the Mac app is
   open it notices the flag, generates the PDF via the existing `PdfExporter`, and clears the flag.
   Not instant (needs the Mac app open at some point after the request), but avoids needing the Mac
   reachable/networked at request time. Not yet implemented — needs a small sheet-based flag +
   polling design before UI work on this piece starts.

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
