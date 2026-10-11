# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Commands

### Build and Run
```bash
# Restore dependencies
dotnet restore src/DrPodcast.csproj

# Build the project
dotnet build src/DrPodcast.csproj --configuration Release

# Run the application (requires API_KEY and BASE_URL environment variables)
dotnet run --project src/DrPodcast.csproj

# Publish as NativeAOT binary
dotnet publish src/DrPodcast.csproj -c Release -r win-x64 --self-contained
```

> NativeAOT publish requires the platform's native toolchain. For a quick local
> build/test loop, pass `-p:PublishAot=false` to skip AOT compilation.

### Test Commands
```bash
# Run all tests
dotnet test tests/DrPodcast.Tests/DrPodcast.Tests.csproj

# Run tests with detailed output
dotnet test tests/DrPodcast.Tests/DrPodcast.Tests.csproj --verbosity normal

# Run tests with code coverage
dotnet test tests/DrPodcast.Tests/DrPodcast.Tests.csproj --collect:"XPlat Code Coverage"

# Watch mode (re-run tests on file changes)
dotnet watch test --project tests/DrPodcast.Tests/DrPodcast.Tests.csproj
```

The test suite includes:
- **PodcastModelsTests.cs**: JSON serialization/deserialization of podcast models
- **PodcastHelpersTests.cs**: Helper functions (image URL extraction)
- **RssBuilderTests.cs**: RSS/iTunes XML construction, audio asset selection, date formatting, byte-exact serialization
- **FeedGenerationServiceTests.cs**: Change detection, asset-hash verification
- **FeedGenerationRunTests.cs**: Whole generation runs against an in-memory DR API (`TestDoubles.cs`): listing recovery after a restart, byte-identical regenerations left untouched, rotated hashes on `Asc` series, timeout attribution, incomplete lists never replacing a fuller feed
- **WebsiteGeneratorTests.cs**: Template rendering, static-asset copying, feed sorting, HTML escaping, exact feed URL on the cards, the first covers loaded eagerly, `descriptions.json` (content, hash-versioned link, untouched when unchanged)
- **FileContainsAsciiTests.cs**: Chunked ASCII search, including needles straddling chunk boundaries
- **DrApiClientTests.cs**: Pagination `next` links are only followed to the DR API host over HTTPS; targeted short-page refetch (failures keep the first pass, episodes without an id aren't duplicated); newest-first latest-episode lookup; incomplete-list warning; the registered client doesn't follow redirects
- **PodcastCatalogTests.cs**: Slugs from DR's series slugs (production-number suffix dropped), collisions, unsafe slugs
- **FeedRefreshBackgroundServiceTests.cs**: Backoff schedule and overflow bounds; `GeneratorConfig` env parsing
- **CachePolicyTests.cs**: `Cache-Control` per file type; only the current asset hash and fonts are immutable

**CI/CD Integration**: Tests run automatically in the build pipeline on all pull requests and pushes to main.

## Coding Conventions

### Scripting Language
- **Preferred scripting language for GitHub Actions workflows: PowerShell (pwsh)**
- PowerShell provides better cross-platform compatibility for Windows, Linux, and macOS runners
- Use `shell: pwsh` in workflow steps
- Only use Bash when PowerShell is not suitable for the specific task
- Keep scripting language consistent within a workflow when possible

### Workflow Guidelines
- Use PowerShell for file manipulation, API calls, and complex logic
- Prefer native PowerShell cmdlets over external tools when available
- Write scripts that work cross-platform (avoid Windows-only cmdlets)

## Architecture Overview

### Core Components
- **Program.cs**: Application entry point — ASP.NET Core host setup, health/readiness endpoints, static file serving
- **FeedGenerationService.cs**: Orchestrator — iterates podcasts in parallel, decides when to skip via `HasNewerEpisodesAsync`/`FeedReferencesLatestAssetAsync`, writes RSS XML atomically, tracks readiness and last-known metadata
- **DrApiClient.cs**: HTTP access to the DR API — fetches series metadata, the latest episode, and paginates full episode lists; registers its own named HttpClient (`DrApiClient.Register`: API key header, timeout, response cap, compression, no redirects, resilience)
- **RssBuilder.cs**: Pure XML construction — builds RSS feeds and episode `<item>` elements, including episode sorting and audio asset selection. Categories are channel-level only: the series' categories as `<itunes:category>`, as DR names them (not mapped to Apple's list); episodes carry none. Episodes also leave out what they share with the channel: no `itunes:author`, `itunes:explicit` only where it differs from the channel's, and `itunes:image` only for an episode's own artwork (not the channel's)
- **FeedRefreshBackgroundService.cs**: Background service — runs feed generation on startup and on a periodic timer with exponential backoff on failure
- **PodcastModels.cs**: Data models and `GeneratorConfig`, with JSON source generation for NativeAOT compatibility
- **PodcastHelpers.cs**: Image URL extraction with priority-based selection
- **WebsiteGenerator.cs**: Generates the static website — copies static assets from `site/` (including the self-hosted fonts in `site/fonts/`, cut down from the variable originals to what the page uses: Bricolage at weight 500 for titles, 77 KB → 20 KB, and Instrument Sans at 400–600; both preloaded) and renders `index.html` from a template: one card per feed with tagline, categories, newest episode and episode count, newest episode first. Clicking a card (or its title button, for keyboards) expands the card into a `<dialog>` with the same content plus the series' full description and the subscribe controls: a copy button, "Åbn i app" links (AntennaPod, Apple Podcasts, Overcast, Pocket Casts) and the feed address. A card's HTML only has its feed link; `script.js` builds the copy button and app links of the expanded card from that link's `href` and hides the link on the shelf, so without JavaScript a card is still its podcast plus a link to its feed. The first six covers load straight away (the first with `fetchpriority="high"`, plus a preconnect to `asset.dr.dk`), the rest lazily. Search waits 100 ms after typing, toggles only the cards whose match changed, and only moves cards when the sort order changes. The search bar is the top of the page (the `<h1>` is visually hidden); the "unofficial" note is in the footer. Descriptions aren't in the page (embedded, they were about half of it compressed): they're written to `descriptions.json` by slug, fetched once on the first open, and linked with a hash of their content (`{{DESCRIPTIONS_VERSION}}`) so the hour it's cached for can't pair a new page with descriptions missing its podcasts; an unchanged file isn't rewritten; where the browser supports view transitions the card morphs into the dialog and back. Podcasts whose first episode is under 30 days old get a "Ny podcast" badge and a "Nye podcasts" filter chip; the date is only published for podcasts in their own right (`Series.IsStandalonePodcast`: DR podcast feed or presentation type "Show"), because DR keeps only a few weeks of its live radio programmes online. `site/script.js` adds search, topic filters, A–Å sorting with Danish collation and relative dates; the page works without it. Cards show DR's 320 px cover (`?imwidth=320`, ~20–30 KB) instead of the 3000×3000 original (~1.2 MB); feeds reference DR's 2048×2048 version (`?imwidth=2048`, within Apple's 1400–3000 px range) via `RssBuilder.FeedArtwork`; both go through `PodcastHelpers.WithImageWidth`. Light/dark follows the system theme (`prefers-color-scheme`). `styles.css` and `script.js` are linked with a content hash (`{{ASSET_VERSION}}`) so their one-hour cache can't pair new markup with old assets. Dates are shown in Danish local time, computed by the EU summer-time rule because the container has no time-zone data. A failed newest-episode lookup is logged and skipped rather than failing the refresh
- **MinimalContentTypeProvider.cs**: Small explicit MIME-type map for static file serving (trim-friendly)
- **CachePolicy.cs**: `Cache-Control` for the static site
- **RegexCache.cs**: Source-generated regex for DR asset-URL parsing
- **PodcastCatalog.cs**: Turns DR's series listing into the podcasts to publish; a feed's slug is DR's slug without its `-{productionNumber}` suffix. News bulletins (presentation type `News`: Radioavisen and the P4 regional news) are left out entirely

### Key Design Patterns
- **NativeAOT Optimization**: Source-generated JSON serialization, trim-safe patterns, aggressive optimization settings
- **Resilient HTTP**: HttpClient configured via `Microsoft.Extensions.Http.Resilience` standard resilience handler (retries, timeouts, circuit breaker). Redirects are not followed: the API key is a custom header, which .NET keeps on a redirect to any host
- **RSS Standards Compliance**: Generates feeds with iTunes and Atom namespaces
- **Pagination Handling**: Fetches all episodes across multiple API pages (256 per page, capped at 100 pages per series). DR sometimes returns short pages while its `next` links still advance by the full page size (in Oct 2026 `tyran` came back with 51 of its 128 episodes), so every page that came back with fewer items than it should (judged against the API's `totalSize`) has its window refetched in 5-item pages, merged by episode id (title + start time for the rare episode without one). Windows the pagination never reached (it stopped before `totalSize`) are fetched the same way. If the refetch fails, what was already fetched is kept
- **Stable Feed URLs**: Subscribers are subscribed to `{BASE_URL}/feeds/{slug}.xml`. `RssBuilder.FeedUrl` is the one place that builds it — for the feed's `atom:self` and the card's feed link, from which `script.js` builds the copy button and every app link — so never change its shape or rename a slug
- **Atomic Writes**: Feeds and `index.html` are written to a `.tmp` file then renamed to avoid serving partial content. `site/index.html` is the *template* and is deliberately excluded from the static-asset copy, so the raw `{{...}}` placeholders are never published
- **Unchanged Feeds Are Not Rewritten**: A regeneration that reproduces the existing file byte for byte leaves it untouched. Static-file ETag and Last-Modified derive from mtime and length, not content, so even an identical rewrite would make every podcatcher download the feed again
- **Never Degrade a Good Feed**: A null series body, or an episode list with no downloadable episodes, is treated as a failed refresh rather than written out — an upstream hiccup must not overwrite a populated feed with an empty one. Likewise, an episode list still short of `totalSize` after the refetch doesn't replace a feed that has more episodes
- **Response Compression**: Brotli/gzip for XML and HTML, at the `Optimal` level (Brotli quality 4) rather than the default `Fastest`: ~10% smaller for ~15–30 ms more on a 3.4 MB feed, paid only by full downloads since most feed requests end in a bodiless 304. Caddy does not compress unless `encode` is configured, so the app does it
- **Cache-Control** (`CachePolicy`): feeds and `index.html` `no-cache` (revalidated on every use, a 304 when unchanged — not left out, which would let caches guess a lifetime from Last-Modified); `styles.css`/`script.js` at the current `?v=` hash and everything under `fonts/` one year, `immutable` — so **a changed font must get a new file name**; anything else one hour
- **Constant-Memory Asset Check**: `FileContainsAsciiAsync` streams the feed in 64 KB pooled chunks (with carry-over across boundaries) instead of decoding it into a string. This runs for every unchanged podcast on every tick; the string version allocated ~4x the feed size per call, all of it on the Large Object Heap
- **Compressed Upstream Fetches**: the DR API client sets `AutomaticDecompression`, which `HttpClient` does not enable by default — episode pages are large JSON and gzip well
- **Change Detection**: Skips regenerating feeds whose `<lastBuildDate>` already matches the API's `LatestEpisodeStartTime`, using `XmlReader` to read only the first few elements; additionally verifies the latest episode's audio asset hash is still referenced (DR rotates hashes without bumping the timestamp) — only for series that published within the last day (`AssetCheckWindow`; about 50 series on a typical day), since with every DR series that check would otherwise be ~680 requests per run. Older episodes are covered by the daily full refresh. The latest episode is requested with `sort=desc`: the endpoint's default order follows the series' `defaultOrder`, so for `Asc` series `limit=1` used to return episode 1. A full regeneration runs on startup and then daily, bounding staleness from what change detection can't see (rotated hashes on older episodes, edited or removed episodes)
- **Cross-Checks Against `LatestEpisodeStartTime`**: `DrApiClient` logs a warning when the latest-episode lookup, or the newest episode in a full fetch, disagrees with the series' own latest timestamp — the signature of both upstream bugs above
- **Resilient Website Listing**: `index.html` is built from the full configured podcast set using last-known metadata, so a transient fetch failure for one podcast doesn't drop its still-served feed from the listing. It is rebuilt whenever a feed changed or the listing differs from the last one rendered (always on a process's first run), so a podcast that failed on the startup run reappears once it recovers
- **Readiness Independent of the Listing**: Readiness is recorded before website regeneration and a listing failure is logged rather than thrown, so a cosmetic `index.html` problem can't 503 a service whose feeds are all current

### Data Flow
1. Discover every DR series (`/radio/v2/series`, ~680) on each run; an incomplete or failed listing falls back to the previous one
2. For each podcast (processed in parallel), use the series data from the listing
3. Compare `LatestEpisodeStartTime` against the existing feed's `<lastBuildDate>` — skip if unchanged (bypassed on startup and the daily full refresh)
4. For an otherwise-skipped feed, verify the newest episode's current audio asset hash is still present; regenerate if it has rotated
5. Paginate through all episodes for the series (up to 256 per page, max 100 pages); refetch short pages in 5-item pages and merge
6. Sort episodes and generate RSS XML with full iTunes metadata
7. Atomically write the feed to `output/_site/feeds/{slug}.xml`, unless it is byte-identical to the existing file
8. Regenerate the static website (`output/_site/index.html`) when any feed changed or the listing differs from the last render

### Environment Variables
| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `API_KEY` | Yes | — | DR API key |
| `BASE_URL` | No | `https://example.com` | Base URL for deployed feeds |
| `REFRESH_INTERVAL_MINUTES` | No | `15` | How often the background service regenerates feeds. Clamped to 1–1440; invalid values fall back to the default |

All three are resolved in `GeneratorConfig` (`FromEnvironment` / `RequireApiKey`) — add new environment variables there rather than reading them at the point of use.

Readiness (`/ready`) reports 503 once the last successful run is older than `max(4 × REFRESH_INTERVAL_MINUTES, 60 minutes)`, so the probe tracks the refresh loop instead of a fixed 24-hour window.

### Project Configuration
- **Target Framework**: .NET 10.0
- **Compilation**: NativeAOT with aggressive trimming and optimization
- **Warning Policy**: Treats warnings as errors (except CS8618 for nullable reference types)
- **Globalization**: Invariant mode for smaller binary size

### Episode Sorting Logic
The application uses different sorting strategies depending on whether a podcast has seasons (see `RssBuilder.BuildRssFeed`):

- **Seasonal shows** (`NumberOfSeries > 0` or `NumberOfSeasons > 0` — DR reports the season count under either field, depending on the series): Sort by season descending (latest first), then by episode order
  - Ascending (`DefaultOrder="Asc"`): Latest season first, then episodes 1→N within season
  - Descending: Latest season first, then episodes N→1 within season
- **Non-seasonal shows**: Standard order without season consideration
  - Ascending: Episodes ordered 1→N by `Order`
  - Descending: Episodes ordered N→1 by `Order`

### Image URL Selection Priority
When extracting image URLs from `ImageAssets` (`PodcastHelpers.cs`):
1. Podcast target with 1:1 ratio (preferred for podcast feeds)
2. Default target with 1:1 ratio
3. Podcast target with any ratio
4. Default target with any ratio
5. Any other target (DR also sends `SquareImage`) as a last resort
6. Falls back to the channel image if an episode has no image at all

In practice every series and episode carries a Podcast 1:1 image (checked Oct 2026), so steps 2–6 are defensive.

### Audio Asset Selection
`RssBuilder.SelectAudioAsset` picks the highest-bitrate `mp3` asset as the enclosure audio (the format is matched **case-insensitively**). Episodes without one are left out of the feed (`RssBuilder.HasDownloadableAudio`): DR offers some episodes, mostly in music-heavy shows, only as an HLS stream, which podcast apps can't play from a feed. The episode count on the website counts the feed's items, not DR's total.

### CI/CD Workflows

#### build-and-release.yml
- **Triggers**: Pushes/PRs to main affecting `src/`, `tests/`, `site/`, or `Dockerfile`; weekly scheduled run
- **Versioning**: Determined by PR body checkboxes — Major / Minor / Patch
- **Docker**: Builds and pushes image to `ghcr.io/{repository}` on merge to main
- **Release**: Creates a GitHub Release tagged with the new version on merge to main
- **PRs**: Build and test only — no push, no release
