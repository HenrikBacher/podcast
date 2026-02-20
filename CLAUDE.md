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
- **PodcastModelsTests.cs**: Tests for JSON serialization/deserialization of podcast models
- **PodcastHelpersTests.cs**: Tests for helper functions (image URL extraction)

**CI/CD Integration**: Tests are automatically executed as part of the build pipeline on all pull requests and pushes to main.

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
- **PodcastFeedGenerator.cs**: Main application entry point — ASP.NET Core host setup, audio proxy endpoint with rate limiting, static file serving
- **FeedGenerationService.cs**: RSS feed generation logic — fetches from DR API, sorts episodes, builds XML, writes atomically to disk
- **FeedRefreshBackgroundService.cs**: Background service — runs feed generation on startup and on a periodic timer with exponential backoff on failure
- **PodcastModels.cs**: Data models with JSON source generation for NativeAOT compatibility
- **PodcastHelpers.cs**: Helper functions for image URL extraction with priority-based selection
- **WebsiteGenerator.cs**: Generates static website with feed listing, manifest.json, and proper HTML escaping
- **podcasts.json**: Configuration file containing podcast slugs and URNs to process (41 Danish podcasts)

### Key Design Patterns
- **NativeAOT Optimization**: Uses source-generated JSON serialization, trim-safe patterns, and aggressive optimization settings
- **Resilient HTTP**: HttpClient configured with Polly retry policies for reliable API calls
- **RSS Standards Compliance**: Generates feeds with iTunes and Atom namespaces
- **Pagination Handling**: Automatically fetches all episodes across multiple API pages (capped at 100 pages per series)
- **Atomic Writes**: Feeds written to a `.tmp` file then renamed to avoid serving partial content
- **Rate Limiting**: Audio proxy endpoint uses ASP.NET Core's built-in sliding window rate limiter (20 req/min per IP), returns `Retry-After` header on rejection
- **Change Detection**: Skips regenerating feeds whose `<lastBuildDate>` already matches the API's `LatestEpisodeStartTime`, using `XmlReader` to read only the first few elements without loading the full document

### Data Flow
1. Load podcast configuration from `podcasts.json`
2. For each podcast (processed in parallel), fetch series metadata from DR API
3. Compare `LatestEpisodeStartTime` against existing feed's `<lastBuildDate>` — skip if unchanged
4. Paginate through all episodes for the series (up to 256 per page, max 100 pages)
5. Sort episodes and generate RSS XML with full iTunes metadata
6. Atomically write feed to `output/_site/feeds/{slug}.xml`
7. Generate static website with feed listing in `output/_site/`
8. Create `manifest.json` with feed metadata and SHA256 hashes

### Environment Variables
| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `API_KEY` | Yes | — | DR API key |
| `BASE_URL` | No | `https://example.com` | Base URL for deployed feeds |
| `PREFER_MP4` | No | `false` | Prefer MP4/M4A audio over MP3; enables audio proxy endpoint |
| `REFRESH_INTERVAL_MINUTES` | No | `15` | How often the background service regenerates feeds |

### Project Configuration
- **Target Framework**: .NET 10.0
- **Compilation**: NativeAOT with aggressive trimming and optimization
- **Warning Policy**: Treats warnings as errors (except CS8618 for nullable reference types)
- **Globalization**: Invariant mode for smaller binary size

### Episode Sorting Logic
The application uses different sorting strategies depending on whether a podcast has seasons:

- **Seasonal shows** (NumberOfSeries > 0): Sort by season descending (latest first), then by episode order
  - Ascending order (DefaultOrder="Asc"): Latest season first, then episodes 1→N within season
  - Descending order: Latest season first, then episodes N→1 within season
  - See `FeedGenerationService.cs` — `BuildRssFeed`

- **Non-seasonal shows**: Standard order without season consideration
  - Ascending order: Episodes ordered 1→N by Order field
  - Descending order: Episodes ordered N→1 by Order field
  - See `FeedGenerationService.cs` — `BuildRssFeed`

### Image URL Selection Priority
When extracting image URLs from ImageAssets (`PodcastHelpers.cs`):
1. Podcast target with 1:1 ratio (preferred for podcast feeds)
2. Default target with 1:1 ratio
3. Podcast target with any ratio
4. Default target with any ratio
5. Falls back to channel image if episode has no suitable image

### Audio Proxy
When `PREFER_MP4=true`, the application serves an `/proxy/audio/{ep}/{asset}` endpoint that:
- Validates `ep` and `asset` are hex strings
- Proxies requests to `https://api.dr.dk/radio/v1/assetlinks/...`
- Rewrites `Content-Type` to `audio/mp4` (correcting the upstream header)
- Forwards `Range` and `User-Agent` headers for seek support
- Rate limits to 20 requests/minute per IP with a `Retry-After` response header on rejection

### CI/CD Workflows

#### build-and-release.yml
- **Triggers**: Pushes/PRs to main affecting `src/`, `tests/`, `site/`, `podcasts.json`, or `Dockerfile`; weekly scheduled run
- **Versioning**: Determined by PR body checkboxes — Major / Minor / Patch
- **Docker**: Builds and pushes image to `ghcr.io/{repository}` on merge to main
- **Release**: Creates a GitHub Release tagged with the new version on merge to main
- **PRs**: Build and test only — no push, no release
