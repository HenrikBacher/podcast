# DrPodcast

DrPodcast is an automated podcast RSS feed generator that fetches podcast data from DR (Danmarks Radio) and generates iTunes-compatible RSS feeds. The project features cross-platform NativeAOT compiled binaries and automated CI/CD workflows for building releases and deploying to GitHub Pages.

This tool currently processes **34 Danish podcasts** from DR's catalog, generating high-quality RSS feeds with full iTunes metadata support.

## Features

- **DR API Integration**: Fetches podcast series and episode data from DR's public API with retry policies
- **iTunes-Compatible RSS**: Generates RSS feeds with full iTunes metadata, categories, and images
- **Episode Management**: Supports both episodic and serial ordering with automatic pagination
- **High Performance**: NativeAOT compiled binaries for fast startup and minimal resource usage
- **Cross-Platform**: Supports Linux (x64, ARM64), Windows (x64), and macOS (Apple Silicon)
- **Automated Deployment**: GitHub Actions workflows for continuous integration and feed generation
- **Web Interface**: Static website for browsing and accessing available podcast feeds
- **Resilient Operation**: Built-in retry logic and error handling for reliable feed generation

## Project Structure

- **[src/](src/)**: C# source code (.NET 9.0)
  - [DrPodcast.csproj](src/DrPodcast.csproj): Project file with NativeAOT and optimization settings
  - [PodcastFeedGenerator.cs](src/PodcastFeedGenerator.cs): Main application entry point and RSS generation logic
  - [PodcastModels.cs](src/PodcastModels.cs): Data models with source-generated JSON serialization
  - [PodcastHelpers.cs](src/PodcastHelpers.cs): Helper functions for category mapping and image URL extraction
  - **Configuration/**: Configuration-related components
  - **Services/**: Service layer components

- **[tests/](tests/)**: Test suite (xUnit)
  - [PodcastModelsTests.cs](tests/DrPodcast.Tests/PodcastModelsTests.cs): Model serialization tests
  - [PodcastHelpersTests.cs](tests/DrPodcast.Tests/PodcastHelpersTests.cs): Helper function tests
  - [FeedGenerationTests.cs](tests/DrPodcast.Tests/FeedGenerationTests.cs): RSS feed generation tests

- **[site/](site/)**: Static website for feed browsing
  - [index.html](site/index.html): Main page listing all available podcast feeds
  - [script.js](site/script.js): Frontend JavaScript for dynamic content
  - [styles.css](site/styles.css): Responsive CSS styles

- **[.github/workflows/](.github/workflows/)**: CI/CD automation
  - [build-and-release.yml](.github/workflows/build-and-release.yml): Cross-platform binary builds and releases
  - [generate-feed.yml](.github/workflows/generate-feed.yml): Automated feed generation and GitHub Pages deployment

- **[podcasts.json](podcasts.json)**: Configuration file containing 34 Danish podcast definitions

## Usage

### Running Locally

#### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download) or later
- DR API key (contact DR for access)

#### Quick Start
```bash
# Clone and navigate to the repository
git clone <repository-url>
cd drpodcast

# Restore dependencies
dotnet restore src/DrPodcast.csproj

# Build the project
dotnet build src/DrPodcast.csproj --configuration Release

# Set environment variables and run
export API_KEY="your-dr-api-key"
export BASE_URL="https://your-domain.com"  # Optional, defaults to https://example.com
dotnet run --project src/DrPodcast.csproj
```

Generated RSS feeds will be saved in the `output/` directory with filenames matching the podcast slugs.

### Using Pre-built Binaries

Download the latest release for your platform from the [Releases](../../releases) page:
- **DrPodcast-linux-x64** - Linux (x64)
- **DrPodcast-linux-arm64** - Linux (ARM64)
- **DrPodcast-win-x64.exe** - Windows (x64)
- **DrPodcast-osx-arm64** - macOS (Apple Silicon)

Run the binary:
```bash
# Linux/macOS (make executable first)
chmod +x DrPodcast-*
API_KEY=<your-api-key> BASE_URL=<base-url> ./DrPodcast-*

# Windows
set API_KEY=<your-api-key>
set BASE_URL=<base-url>
DrPodcast-win-x64.exe
```

## Testing

DrPodcast includes a comprehensive test suite built with xUnit and FluentAssertions.

### Running Tests
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

### Test Coverage
The test suite includes 52 tests covering:
- **Model Serialization**: JSON deserialization of podcast models, episodes, and metadata
- **Helper Functions**: Category mapping, image URL extraction, and priority selection
- **RSS Feed Generation**: XML structure validation, namespaces, and iTunes metadata
- **Date/Duration Formatting**: RFC 822 date format and duration string generation
- **Edge Cases**: Null handling, empty collections, and fallback behavior

### CI/CD Integration
Tests are automatically executed as part of the build pipeline:
- ✅ Run on all pull requests before merge
- ✅ Run on pushes to main branch
- ✅ Test results uploaded as artifacts for review
- ✅ Code coverage reports generated for each platform

## Configuration

### Environment Variables
- `API_KEY`: DR API key (required)
- `BASE_URL`: Base URL for the deployed feeds (default: `https://example.com`)

### Adding Podcasts
Edit [podcasts.json](podcasts.json) to add or remove podcasts. Each podcast requires:
- **slug**: URL-friendly identifier used for the RSS filename
- **urn**: DR's unique identifier for the podcast series

```json
{
  "podcasts": [
    {
      "slug": "example-podcast",
      "urn": "urn:dr:radio:series:5fa25ef8330eac2f135b62d6"
    }
  ]
}
```

To find a podcast's URN, inspect DR's website network requests or contact DR for the appropriate series identifier.

## Technical Details

- **Runtime**: .NET 9.0 with NativeAOT compilation for optimal performance
- **Binary Size**: Small, self-contained executables (~15-30MB per platform)
- **HTTP Resilience**: Polly retry policies with exponential backoff for API reliability
- **JSON Processing**: Source-generated serialization for trim-safe, high-performance parsing
- **RSS Standards**: Compliant with RSS 2.0, iTunes, Atom, and Media RSS specifications
- **Memory Efficiency**: Streaming processing for large podcast catalogs
- **Cross-Platform**: Native binaries for Linux (x64/ARM64), Windows (x64), and macOS (ARM64)

## CI/CD Workflows

### Build and Release (`build-and-release.yml`)
Automatically triggered on:
- Pushes to `main` branch
- Pull requests affecting `src/` or `tests/` directories

**Process:**
- Semantic versioning from commit messages and PR labels
- Dependency restoration and project build
- **Automated test execution** with code coverage collection
- Test results uploaded as artifacts (retained for 7 days)
- Cross-platform NativeAOT compilation (Linux x64/ARM64, Windows x64, macOS ARM64)
- Automated GitHub releases (prereleases for PRs, stable for main)
- Artifact cleanup and prerelease management

### Feed Generation (`generate-feed.yml`)
Runs on schedule and configuration changes:
- **Schedule**: Hourly automated execution
- **Triggers**: Changes to `podcasts.json`

**Process:**
- Downloads latest release binary
- Generates RSS feeds for all 34 configured podcasts
- Hash-based change detection to minimize unnecessary deployments
- GitHub Pages deployment with caching and optimization

## License

This project is provided as-is for educational purposes.