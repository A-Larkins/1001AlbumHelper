# 1001 Albums Helper

A personal tool for working through the **"1001 Albums You Must Hear Before You Die"** list —
rating albums as you listen, curating a shortlist of potential replacements, and building real
Apple Music playlists to listen from. Runs as both a **Mac desktop app** and an **iPhone app**,
built from one shared C# codebase, with a Google Sheet as the thing they both sync through.

The workflow it exists to serve: queue a few albums into Apple Music, listen, rate them on whichever
device is to hand, clear them out, repeat — without keeping any of the bookkeeping in your head.

---

## Contents

- [What it does](#what-it-does)
- [The two apps](#the-two-apps)
- [Architecture](#architecture)
- [How the data flows](#how-the-data-flows)
- [Apple Music integration](#apple-music-integration)
- [Building and running](#building-and-running)
- [Configuration](#configuration)
- [Diagnostics](#diagnostics)
- [Known constraints](#known-constraints)

---

## What it does

**Rate the list.** Walk the 1001 one album at a time and give each a ⭐ / 👍 / 👎 / ❌. Three
queues decide what comes up: **Next up** (never listened), **Backfill ✓** (marked listened but never
rated) and **Revisit** (already rated, optionally filtered to one symbol) — so a rating is never
write-once. Each album's card shows the sleeve, fetched through the same Apple Music lookup the
playlist export uses, so the art you see belongs to the copy you'd actually queue.

A ⭐ has a side effect, and it runs both ways: earning one adds the album to the "Must Hear" list,
losing one takes it off again and renumbers what's left.

**Keep a shortlist of replacements.** Albums you think belong on the list instead. Add them by hand
with Discogs autocomplete filling in artist and year, or let them arrive on their own — anything you
queue into Apple Music's PLAYLIST2 gets picked up on the next pull and offered as a candidate. Each
one waits for a **Keep** or a **Nah**, and a decision is never silently undone.

Years are dated to the album's **first** release, not to whichever reissue Discogs knows best: a 1970
record remastered in 2010 is a 1970 record.

**Build listening playlists.** Two working lists — Playlist 1 from the 1001, Playlist 2 for
recommendations — that push straight into real Apple Music playlists of the same name, and pull back
down to match what's actually there.

**Sync across devices.** Ratings and the shortlist live in a Google Sheet, so the Mac and the phone
see the same thing without either being the master.

**Export.** The combined "must hear" + replacements list to PDF, plus Excel/CSV.

---

## The two apps

Both heads are thin — an entry point plus the platform's Avalonia backend — and share every screen's
logic. Desktop shows **windows**; iPhone shows a **single view with five bottom tabs** (phones don't
do windows).

| | Mac | iPhone |
|---|:---:|:---:|
| Rate albums (all three queues) | ✅ | ✅ |
| Shuffle the rating queue | ✅ | — |
| Browse 1001 / Must Hear / Replacements | ✅ | ✅ |
| Shortlist: add, keep, drop, edit years | ✅ | ✅ |
| Playlists: queue, push, pull | ✅ | ✅ |
| Google Sheets sync | ✅ | ✅ |
| Apple Music | Music.app via AppleScript | MediaPlayer framework |
| Per-track playability picking | ✅ | n/a (see below) |
| PDF / Excel / CSV export | ✅ | — |
| Analytics charts | ✅ | — |

The phone ships with **embedded CSV/JSON snapshots** of the list baked in at build time, so it opens
instantly and works with no signal. The 1001 tab shows that snapshot first, then quietly replaces it
with the live sheet — instant and offline-safe, but never stale.

---

## Architecture

```
1001AlbumHelper.sln
├── 1001AlbumHelper/            shared library — all the real code
│   ├── Models/                 Album, ViewRow, RatingSession, …
│   ├── Data/                   NumberedList, ReplacementCandidates, PlaylistStore,
│   │                           MobileData, ShortlistIntake
│   ├── Integrations/           Discogs, Google Sheets, Apple Music, artwork
│   ├── Export/                 PDF / Excel / CSV
│   ├── Views/Desktop/          the Mac windows
│   └── Views/Mobile/           MainView + the five phone screens, and MobileTheme
├── 1001AlbumHelper.Desktop/    Mac/Windows/Linux head (Avalonia desktop)
├── 1001AlbumHelper.iOS/        iPhone head (Avalonia iOS, bundle id com.larkins.albumhelper)
└── 1001AlbumHelper.Tests/      xUnit — 171 tests, no network
```

A few decisions worth knowing before changing anything:

**The mobile look lives in one file.** `Views/Mobile/MobileTheme.axaml` holds the phone's whole
design system — palette, type scale, and every control's look. It's included by `MainView`, *not* by
`App.axaml`, because `App` is shared with the Mac head; that scoping is what lets the phone be
restyled without touching a single desktop window.

**Sheets access is hand-rolled.** Sync goes through the Google Sheets REST API directly with a
service-account JWT (no `Google.Apis`, no OAuth screen). That's what lets the phone sync with no
browser sign-in — and it's trim/AOT-safe on iOS, which `Google.Apis` is not.

**Apple Music sits behind one interface**, implemented per platform. Album choice runs through the
same `AppleMusicCatalog.FindBestMatch` on both, so each prefers the original release over a
"(Deluxe Edition)" identically.

**Tests cover the judgement calls, not the plumbing.** The rules that decide which album Discogs
means, which pressing to add, and how two copies of a shortlist merge are all unit-tested without a
network — because getting those wrong doesn't crash anything, it just quietly writes something wrong.

---

## How the data flows

```
                  ┌──────────────────┐
    Mac  ────────►│   Google Sheet   │◄──────── iPhone
                  │  ratings +       │
                  │  shortlist       │
                  └──────────────────┘
                          ▲
                          │  Keep / rate / edit
                          │
    Mac  ──push/pull──►  Apple Music  ◄──push/pull── iPhone
                       PLAYLIST1 / PLAYLIST2
```

Both devices hold the shortlist for as long as a screen is open and neither is told when the other
changes it, so **a write that shortens the list is always a stale copy winning**. Nothing in the app
ever deletes a candidate — rows are appended and a decision is a status change on a row that stays —
so pushes read the sheet and **merge** into it rather than replacing it: the writer's row wins where
both know an album, but a year or genre comes from whichever side has one, and rows the writer never
heard of survive.

A pull of PLAYLIST2 hands the **whole** playlist to the shortlist intake, not just what that device
found new. Newness is per-device and gets consumed by whoever pulls first; comparing against the
shortlist itself gives the same answer on either device, however many times it runs.

---

## Apple Music integration

Per-platform behind one interface, because the two systems can do different things.

**iPhone** — Apple's public `MediaPlayer` framework, so no paid developer entitlement is needed. It
resolves a store id via the iTunes Search API and adds by id.

**Mac** — drives Music.app over AppleScript (`osascript`), asking Music to `search` and duplicating
the resulting tracks. With Sync Library on, that search reaches the **whole Apple Music catalogue**,
so obscure 1001 entries resolve just like on the phone, with no iTunes rate limit in play.

**Both are add-only.** There is no public API to remove a track from a playlist, so removals become a
checklist you work through by hand in Apple Music and tick off in the app.

**Picking the copy that plays.** Apple Music stocks some albums twice under identical names, and the
copies aren't equally alive — same name, same artist, same track numbers, same release date, and five
tracks "no longer available" on one of them. Nothing in the metadata separates them, so the Mac's
search carries each track's cloud status and picks one copy per slot, preferring one that will
actually play. A song no copy can play is named in the result rather than quietly missing. The iPhone
is untouched here: the iTunes Search API reports one collection and no playability at all, so there
is nothing to choose between.

---

## Building and running

Requires the .NET SDK. The phone build additionally needs the iOS workload and Xcode for signing.

```bash
# Mac / Windows / Linux desktop app
dotnet run --project 1001AlbumHelper.Desktop

# The classic text menu instead of the GUI
dotnet run --project 1001AlbumHelper.Desktop -- console

# Tests
dotnet test 1001AlbumHelper.Tests

# The phone UI in a phone-sized window, without a device deploy
dotnet run --project 1001AlbumHelper.Desktop -- mobilepreview
# …opening straight onto one tab (0-4: List, Rate, Shortlist, Playlist 1, Playlist 2)
ALBUMHELPER_PREVIEW_TAB=1 dotnet run --project 1001AlbumHelper.Desktop -- mobilepreview
```

**iPhone.** Plug the phone in, unlock it, and double-click **`Re-deploy to iPhone.command`** in the
repo root. It renews the 7-day signing profile, rebuilds, reinstalls and launches. The underlying
build flags are all load-bearing — see [`PROJECT.md`](PROJECT.md) §4 before running it by hand.

---

## Configuration

Google Sheets sync and Discogs lookups need your own credentials in `1001AlbumHelper/appsettings.json`
(not committed — copy `appsettings.example.json`). **The app runs fine without them, just local-only.**

| Key | What it's for |
|---|---|
| `Discogs:Token` | Album/artist lookups and year backfill |
| `GoogleSheets:SpreadsheetId` | The workbook both devices sync through |
| `GoogleSheets:AlbumsTab` | The master 1001 list (ratings live here) |
| `GoogleSheets:PotentialsTab` | The replacements shortlist |
| `GoogleSheets:StarredTab` / `ReplacementsTab` | Derived "Must Hear" and replacements lists |
| `GoogleSheets:ServiceAccountKeyFile` | Service-account JSON key — this is what avoids an OAuth screen |

On the phone the config and key are loaded from **embedded resources** baked into the assembly, since
there's no filesystem to drop them into.

---

## Diagnostics

Headless checks that verify a subsystem end-to-end against the real services. The write-capable ones
are self-cleaning and never touch real data.

```bash
dotnet run --project 1001AlbumHelper.Desktop -- synctest         # Sheets potentials sync
dotnet run --project 1001AlbumHelper.Desktop -- resttest         # the mobile-safe REST Sheets client (scratch tab)
dotnet run --project 1001AlbumHelper.Desktop -- musictest        # the Mac's Apple Music path (scratch playlist)
dotnet run --project 1001AlbumHelper.Desktop -- intaketest       # rehearse a PLAYLIST2 pull, writing nothing
dotnet run --project 1001AlbumHelper.Desktop -- backfill-genres  # fill missing genres via Discogs (resumable)
```

---

## Known constraints

- **7-day signing.** A free Apple account's profile expires weekly, so the phone app must be
  re-deployed every 7 days. The $99/yr Developer Program removes that and enables TestFlight.
- **Apple Music removals are manual** on both platforms — see above.
- **Discogs is rate-limited** to 60 requests/minute, so year and genre backfills are paced at roughly
  one a second and report their way down the list.

---

For the full internal handbook — build flags, deploy steps, every gotcha we hit and why, and the
running version history — see [`PROJECT.md`](PROJECT.md).
