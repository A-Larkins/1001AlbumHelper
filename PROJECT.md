# 1001 Albums Helper — Project Handbook

A living reference for the app: what it is, how it's built, how to ship it, and where it's going.
**Update this as we go.** Last updated: 2026-07-28.

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
│   │                           CandidateRepository, AppleMusicCatalog, SyncDiagnostic
│   ├── Export/                 CsvGenerator, ExcelGenerator, PdfExporter
│   ├── Views/Desktop/          the six desktop windows
│   ├── Views/Mobile/           MainView + AlbumListView, ReplacementsView, PlaylistView
│   ├── Console/                ConsoleMenu (text menu; `dotnet run -- console`)
│   └── input/ output/ appsettings.json  (data + config; git-ignored where secret)
│
├── 1001AlbumHelper.Desktop/    → Mac/Win/Linux head (produces the "1001AlbumHelper" executable)
├── 1001AlbumHelper.iOS/        → iPhone head (bundle id: com.larkins.albumhelper)
└── 1001AlbumHelper.Tests/      → xUnit tests (62 as of this writing)
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

### In progress / close (this session)

- **Apple Music linked to your existing `PLAYLIST1` / `PLAYLIST2`** (by name), instead of creating
  new ones. Add + read work; remove is not possible in Apple Music (see §6).
- **Mobile Google Sheets sync** — the working lists syncing Mac ↔ phone (next: embed the service
  account key on iOS).

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

### The big constraint: add-only

Apple's MediaPlayer framework can **add** items to a playlist but has **no API to remove, clear, or
delete** — for any playlist. "Wipe and remake" doesn't work either (there's no wipe). The only
framework that can truly edit a playlist is **MusicKit**, which is Swift-only and unreachable from
C#/.NET.

**So the model is:** the app's Playlist tabs are your **clean working list** (full add/remove, and
— next — synced via Sheets); Apple Music is an **add-only listening queue**. You occasionally clear
the Apple Music playlist yourself; everything else is automated.

---

## 6. Updating the potentials list (Google Sheets sync)

Keeps the potentials shortlist in sync across devices via a **"Potentials" tab** in the Google Sheet.

- Auth is a **Google service account** (works on Mac *and* iPhone with no login screen — unlike the
  browser OAuth the desktop list-writer uses). Key file: `1001AlbumHelper/service-account.json`
  (git-ignored). Share the sheet with the service account email as **Editor**.
  - Service account: `albums-sync@secret-319922.iam.gserviceaccount.com`.
- Config lives in `appsettings.json` → `GoogleSheets:{PotentialsTab, ServiceAccountKeyFile}`.
- Code: `CandidateSheet` (read/write the tab) + `CandidateRepository` (Sheets-when-configured, local
  JSON as offline cache). First sync **seeds** the sheet from local so an empty sheet never wipes it.
- Verify / seed headlessly: `dotnet run --project 1001AlbumHelper.Desktop -- synctest`.

**Status:** working + verified on desktop. iPhone side (embed the key) is the next step.

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
- **No app icon on iOS** yet — the home-screen icon is a blank placeholder. Easy to add.
- **Mobile Sheets sync not wired** — the phone reads baked-in snapshots; changes on the phone don't
  yet reach the Mac. (Next up.)
- **Apple Music removal** is impossible (see §5).

---

## 8. Future features & ideas

- **Finish the Apple Music playlist workflow** — ＋P1/＋P2 push straight into PLAYLIST1/PLAYLIST2 as
  you browse; import existing contents to seed the working list.
- **Mobile Sheets sync** — embed the service-account key on iOS so the working lists sync Mac ↔ phone.
- **App icon** for the iPhone app.
- **Weekly re-deploy pain** — a small script, or spring for the $99 Apple Developer Program +
  TestFlight (install once, no 7-day expiry).

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
