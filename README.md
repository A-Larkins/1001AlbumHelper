# 1001 Albums Helper

A personal tool for working through the **"1001 Albums You Must Hear Before You Die"** list —
rating albums, curating a shortlist of potential replacement albums, and building Apple Music
listening playlists. Runs as both a **Mac desktop app** and an **iPhone app**, built from one shared
C# codebase.

## What it does

- Tracks your progress through the 1001 list and lets you rate albums (⭐ / 👍 / 👎 / ❌) as you listen —
  and change a rating later, either by walking the albums you've already rated or by jumping straight
  to one from the browse list (Mac) or the find box (iPhone).
- Keeps a shortlist of potential replacement albums, with Discogs lookups for missing years.
- Builds two working playlists (from the 1001, and recommendations) and pushes them straight into
  real Apple Music playlists — from the phone (MediaPlayer) or the Mac (Music.app over AppleScript).
- Syncs the shortlist and ratings across devices via a Google Sheet.
- Exports the combined "must hear" + replacements list to PDF.

## Architecture

```
1001AlbumHelper.sln
├── 1001AlbumHelper/            shared library — all the real code (Models, Data, Integrations,
│                               Export, Views/Desktop, Views/Mobile)
├── 1001AlbumHelper.Desktop/    Mac/Windows/Linux head (Avalonia desktop)
├── 1001AlbumHelper.iOS/        iPhone head (Avalonia iOS)
└── 1001AlbumHelper.Tests/      xUnit tests
```

Both heads are thin — an entry point plus the platform's Avalonia backend. Desktop shows windows;
iPhone shows a single view with bottom tabs (phones don't do windows).

**Sync** goes through the Google Sheets REST API directly with a service-account JWT (no
Google.Apis, no OAuth login screen) — that's what lets the phone sync with no browser sign-in.

**Apple Music** integration is per-platform behind one interface: the iPhone uses Apple's public
`MediaPlayer` framework (no paid developer entitlement needed), and the Mac drives Music.app over
AppleScript. MediaPlayer is *add-only* — there's no public API to remove tracks from a playlist —
so the app tracks removals as a checklist you work through by hand in Apple Music.

See [`PROJECT.md`](PROJECT.md) for the full internal handbook — build flags, deploy steps, known
gotchas, and the current roadmap.

## Building

Requires the .NET SDK with the iOS workload for the phone build, and Xcode for signing.

```bash
# Desktop (Mac/Windows/Linux)
dotnet run --project 1001AlbumHelper.Desktop

# Tests
dotnet test 1001AlbumHelper.Tests

# iPhone (see PROJECT.md §4 for the full deploy flow, signing, and required build flags)
dotnet build 1001AlbumHelper.iOS -c Debug -f net10.0-ios -r ios-arm64
```

Google Sheets sync and Discogs lookups need your own credentials in `appsettings.json` (not
committed) — the app runs fine without them, just local-only.

## Status

Actively developed, personal-use project. Not affiliated with Apple, Google, or Discogs.
