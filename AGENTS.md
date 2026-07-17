# AGENTS.md

This file provides guidance to coding agents (Claude Code, etc.) when working with code in this repository.

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
- **RssBuilderTests.cs**: RSS/iTunes XML construction, audio asset selection, date formatting
- **FeedGenerationServiceTests.cs**: Change detection, asset-hash verification, success threshold

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
- **DrApiClient.cs**: HTTP access to the DR API — fetches series metadata, the latest episode, and paginates full episode lists
- **RssBuilder.cs**: Pure XML construction — builds RSS feeds and episode `<item>` elements, including episode sorting and audio asset selection
- **FeedRefreshBackgroundService.cs**: Background service — runs feed generation on startup and on a periodic timer with exponential backoff on failure
- **PodcastModels.cs**: Data models and `GeneratorConfig`, with JSON source generation for NativeAOT compatibility
- **PodcastHelpers.cs**: Image URL extraction with priority-based selection
- **WebsiteGenerator.cs**: Generates the static website — copies static assets from `site/` and renders `index.html` from a template with the feed listing
- **MinimalContentTypeProvider.cs**: Small explicit MIME-type map for static file serving (trim-friendly)
- **RegexCache.cs**: Source-generated regex for DR asset-URL parsing
- **podcasts.json**: Configuration file containing podcast slugs and URNs to process (Danish podcasts)

### Key Design Patterns
- **NativeAOT Optimization**: Source-generated JSON serialization, trim-safe patterns, aggressive optimization settings
- **Resilient HTTP**: HttpClient configured via `Microsoft.Extensions.Http.Resilience` standard resilience handler (retries, timeouts, circuit breaker)
- **RSS Standards Compliance**: Generates feeds with iTunes and Atom namespaces
- **Pagination Handling**: Fetches all episodes across multiple API pages (256 per page, capped at 100 pages per series)
- **Atomic Writes**: Feeds and `index.html` are written to a `.tmp` file then renamed to avoid serving partial content
- **Change Detection**: Skips regenerating feeds whose `<lastBuildDate>` already matches the API's `LatestEpisodeStartTime`, using `XmlReader` to read only the first few elements; additionally verifies the latest episode's audio asset hash is still referenced (DR rotates hashes without bumping the timestamp)
- **Resilient Website Listing**: `index.html` is built from the full configured podcast set using last-known metadata, so a transient fetch failure for one podcast doesn't drop its still-served feed from the listing

### Data Flow
1. Load podcast configuration from `podcasts.json` (parsed once at startup)
2. For each podcast (processed in parallel), fetch series metadata from the DR API
3. Compare `LatestEpisodeStartTime` against the existing feed's `<lastBuildDate>` — skip if unchanged
4. For an otherwise-skipped feed, verify the latest episode's current audio asset hash is still present; regenerate if it has rotated
5. Paginate through all episodes for the series (up to 256 per page, max 100 pages)
6. Sort episodes and generate RSS XML with full iTunes metadata
7. Atomically write the feed to `output/_site/feeds/{slug}.xml`
8. Regenerate the static website (`output/_site/index.html`) when any feed changed or on a forced startup run

### Environment Variables
| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `API_KEY` | Yes | — | DR API key |
| `BASE_URL` | No | `https://example.com` | Base URL for deployed feeds |
| `REFRESH_INTERVAL_MINUTES` | No | `15` | How often the background service regenerates feeds |

### Project Configuration
- **Target Framework**: .NET 10.0
- **Compilation**: NativeAOT with aggressive trimming and optimization
- **Warning Policy**: Treats warnings as errors (except CS8618 for nullable reference types)
- **Globalization**: Invariant mode for smaller binary size

### Episode Sorting Logic
The application uses different sorting strategies depending on whether a podcast has seasons (see `RssBuilder.BuildRssFeed`):

- **Seasonal shows** (`NumberOfSeries > 0`): Sort by season descending (latest first), then by episode order
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
5. Falls back to the channel image if an episode has no suitable image

### Audio Asset Selection
`RssBuilder.SelectAudioAsset` picks the highest-bitrate `mp3` asset as the enclosure audio (the format is matched **case-insensitively**).

### CI/CD Workflows

#### build-and-release.yml
- **Triggers**: Pushes/PRs to main affecting `src/`, `tests/`, `site/`, `podcasts.json`, or `Dockerfile`; weekly scheduled run
- **Versioning**: Determined by PR body checkboxes — Major / Minor / Patch
- **Docker**: Builds and pushes image to `ghcr.io/{repository}` on merge to main
- **Release**: Creates a GitHub Release tagged with the new version on merge to main
- **PRs**: Build and test only — no push, no release
