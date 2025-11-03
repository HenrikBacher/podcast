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
- **PodcastModels.cs**: Data models for JSON serialization
- **PodcastHelpers.cs**: Helper functions for category mapping and image URL extraction
- **podcasts.json**: Configuration file containing podcast slugs and URNs to process

### Key Design Patterns
- **Resilient HTTP**: HttpClient configured with Polly retry policies for reliable API calls
- **RSS Standards Compliance**: Generates feeds with iTunes, Atom, and Media RSS namespaces
- **Pagination Handling**: Automatically fetches all episodes across multiple API pages

### Data Flow
1. Load podcast configuration from `podcasts.json`
2. For each podcast, fetch series metadata from DR API
3. Paginate through all episodes for the series
4. Generate RSS XML with full iTunes metadata
5. Output feeds to `output/` directory

### Environment Variables
- `API_KEY`: DR API key (required)
- `BASE_URL`: Base URL for deployed feeds (default: "https://example.com")

### Project Configuration
- **Target Framework**: .NET 9.0
- **Compilation**: Self-contained single-file executables
- **Warning Policy**: Treats warnings as errors (except CS8618 for nullable reference types)