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
- **PodcastHelpersTests.cs**: Tests for helper functions (category mapping, image URL extraction)
- **FeedGenerationTests.cs**: Tests for RSS feed XML generation and structure validation

**CI/CD Integration**: Tests are automatically executed as part of the build pipeline on all pull requests and pushes to main. Test results and code coverage are uploaded as artifacts for review.

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
- **PodcastFeedGenerator.cs**: Main application entry point and RSS feed generation logic
- **PodcastModels.cs**: Data models with JSON source generation for NativeAOT compatibility
- **PodcastHelpers.cs**: Helper functions for image URL extraction with priority-based selection
- **WebsiteGenerator.cs**: Generates static website with feed listing, manifest.json, and proper HTML escaping
- **podcasts.json**: Configuration file containing podcast slugs and URNs to process (35 Danish podcasts)

### Key Design Patterns
- **NativeAOT Optimization**: Uses source-generated JSON serialization, trim-safe patterns, and aggressive optimization settings
- **Resilient HTTP**: HttpClient configured with Polly retry policies for reliable API calls
- **RSS Standards Compliance**: Generates feeds with iTunes, Atom, and Media RSS namespaces
- **Pagination Handling**: Automatically fetches all episodes across multiple API pages

### Data Flow
1. Load podcast configuration from `podcasts.json`
2. For each podcast (processed in parallel), fetch series metadata from DR API
3. Paginate through all episodes for the series (up to 256 per page)
4. Generate RSS XML with full iTunes metadata (iTunes, Atom, Media RSS namespaces)
5. Save feeds to `output/_site/feeds/` directory
6. Generate static website with feed listing in `output/_site/`
7. Create `manifest.json` with feed metadata and SHA256 hashes for change detection

### Environment Variables
- `API_KEY`: DR API key (required)
- `BASE_URL`: Base URL for deployed feeds (default: "https://example.com")

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
  - See PodcastFeedGenerator.cs:144-146

- **Non-seasonal shows**: Standard order without season consideration
  - Ascending order: Episodes ordered 1→N by Order field
  - Descending order: Episodes ordered N→1 by Order field
  - See PodcastFeedGenerator.cs:151-153

### Image URL Selection Priority
When extracting image URLs from ImageAssets (PodcastHelpers.cs:5-16):
1. Podcast target with 1:1 ratio (preferred for podcast feeds)
2. Default target with 1:1 ratio
3. Podcast target with any ratio
4. Default target with any ratio
5. Falls back to channel image if episode has no suitable image

### CI/CD Workflows

#### build-and-release.yml
- **Triggers**: Pushes/PRs to main affecting `src/` or `tests/` directories
- **Semantic Versioning**: Determined by commit messages or PR body
  - `[major]` or "breaking change" → major version bump
  - `[minor]` or "feature" → minor version bump
  - Default → patch version bump
- **Build Matrix**: Cross-platform builds (Linux x64/ARM64, Windows x64, macOS ARM64)
- **Test Execution**: Runs full test suite with code coverage on all platforms
- **Artifacts**: Binaries with SHA256 checksums, retained for 30 days
- **Prerelease Management**: PRs create prereleases (v1.2.3-pr.X.HASH), deleted when stable release is created

#### generate-feed.yml
- **Triggers**: Hourly cron schedule, workflow_dispatch, or changes to `podcasts.json`/`site/`
- **Runner**: Uses ubuntu-24.04-arm for cost efficiency
- **Deployment Logic**: Hash-based change detection using manifest.json to avoid unnecessary deployments
- **GitHub Pages**: Deploys to Pages only if feed content has changed (compares SHA256 hashes)
- **Manual Override**: Supports `use_prerelease` input to test with prerelease binaries