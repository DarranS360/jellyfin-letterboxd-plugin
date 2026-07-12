# Letterboxd Collections

A Jellyfin plugin that automatically creates and refreshes native Jellyfin collections from a
[Letterboxd](https://letterboxd.com) user's public lists and watchlist - on Jellyfin's own
scheduled task runner, no external script or container required.

## Features

- Auto-discovers every public list on a Letterboxd profile (no need to configure each one by hand)
- Optionally syncs the watchlist as its own collection
- Matches films against your existing library by title/year - nothing is downloaded or requested
- Fully re-syncs on each run: collections track additions *and* removals on the Letterboxd side
- Runs as a normal Jellyfin scheduled task (Dashboard -> Scheduled Tasks), so you control the schedule
- Optional built-in poster-strip cover art generator for collections without an image

## Installation

**Via a plugin repository:**

1. Dashboard -> Plugins -> Repositories -> Add Repository
2. Add `https://raw.githubusercontent.com/DarranS360/jellyfin-letterboxd-plugin/master/manifest.json`
3. Dashboard -> Plugins -> Catalog -> install "Letterboxd Collections"
4. Restart Jellyfin

**Manual install:** download the zip from [Releases](https://github.com/DarranS360/jellyfin-letterboxd-plugin/releases),
extract into your Jellyfin `plugins/Letterboxd Collections_<version>/` folder, and restart Jellyfin.

## Configuration

Dashboard -> Plugins -> Letterboxd Collections:

| Setting | Description |
|---|---|
| Letterboxd username | The public profile to sync from |
| Sync watchlist | Creates a "&lt;username&gt; Watchlist" collection |
| Auto-discover lists | Scans the user's lists page and syncs every list found |
| Excluded list slugs | Comma-separated slugs to skip (see a list's URL for its slug) |
| Remove films dropped from a list | Off = only ever adds; on = full two-way sync |
| Generate cover art | Builds a cover image for collections without one |

Then run the "Sync Letterboxd Collections" task once from Dashboard -> Scheduled Tasks to sync
immediately, or wait for its default 6-hour interval.

## Notes

- There's no official Letterboxd API - this reads the same public HTML a browser would.
- Collection names generally match the Letterboxd list title. Very generic names (e.g. a list
  literally titled "Animated") can occasionally get misidentified by Jellyfin's own metadata
  scanner as an unrelated TMDB franchise collection and have their name/overview overwritten.
  If that happens, add the list's slug to "Excluded list slugs" and re-add it under a more
  specific name, or disable internet metadata providers for BoxSets under
  Dashboard -> Libraries -> Collections -> Manage -> Metadata.
