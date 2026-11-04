# Working on 1001 Albums Helper

Read `PROJECT.md` first — it's the handbook (architecture, history, gotchas, the weak-points checklist).

## House rules

- **Reinstall the app after every change.** When a piece of work is done and the tests pass, run
  `./1001AlbumHelper/build-macos-app.sh --install` (from the checkout or worktree that holds the work),
  quit any running copy (`osascript -e 'tell application "1001 Albums Helper" to quit'`), and reopen it
  from /Applications. The installed app is what gets tested — don't leave it stale.
- **American English everywhere** — app text, code, comments, docs, commit messages ("favorite",
  "color", "catalog"). Album titles keep their own spelling. Say "Apple Music" in labels.
- **Never drive the app with keystrokes to look at it.** The Rate page rates on bare 1–4 and writes to
  the real sheet. Launch with `ALBUMHELPER_START_SECTION=Browse` (Rate, Browse, Shortlist, Playlists,
  Export, Insights, Tools) instead.
- **Anything that writes to the Google Sheet or the Apple Music library is real.** Test against
  `FakeSheets` / the `musictest` scratch playlist, and use `--dry-run` where a command offers it.
