# DrPodcast

DrPodcast is an automated podcast RSS feed generator that fetches podcast data from DR (Danmarks Radio) and generates iTunes-compatible RSS feeds. The project includes automated CI/CD workflows for building cross-platform binaries and deploying to GitHub Pages.

## Features

- Fetches podcast series and episode data from DR's public API
- Generates iTunes-compatible RSS feeds with full metadata
- Supports episode ordering (episodic vs. serial)
- Handles pagination for podcasts with many episodes
- Includes podcast categories, images, and episode metadata
- Cross-platform native AOT compiled binaries (Linux, Windows, macOS)
- Automated feed generation and deployment via GitHub Actions
- Static website for browsing available feeds

## Project Structure

- **[src/](src/)**: C# source code (.NET 9.0)
  - [DrPodcast.csproj](src/DrPodcast.csproj): Project file with NativeAOT settings
  - [PodcastFeedGenerator.cs](src/PodcastFeedGenerator.cs): Main feed generation logic
  - [PodcastModels.cs](src/PodcastModels.cs): Data models with JSON source generation

- **[site/](site/)**: Static website frontend
  - [index.html](site/index.html): Main page listing available feeds
  - [script.js](site/script.js): Frontend JavaScript
  - [styles.css](site/styles.css): Styles

- **[.github/workflows/](.github/workflows/)**: CI/CD automation
  - [build-and-release.yml](.github/workflows/build-and-release.yml): Builds and releases cross-platform binaries
  - [generate-feed.yml](.github/workflows/generate-feed.yml): Generates feeds and deploys to GitHub Pages

- **[podcasts.json](podcasts.json)**: Configuration file with podcast slugs and URNs

## Usage

### Running Locally

#### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download)
- API key from DR (set as environment variable)

#### Build and Run
```bash
# Restore dependencies
dotnet restore src/DrPodcast.csproj

# Build the project
dotnet build src/DrPodcast.csproj --configuration Release

# Run the feed generator
API_KEY=<your-api-key> BASE_URL=<base-url> dotnet run --project src/DrPodcast.csproj
```

Generated RSS feeds will be saved in the `output/` directory.

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

## Configuration

### Environment Variables
- `API_KEY`: DR API key (required)
- `BASE_URL`: Base URL for the deployed feeds (default: `https://example.com`)

### Adding Podcasts
Edit [podcasts.json](podcasts.json) to add or remove podcasts:
```json
{
  "podcasts": [
    {
      "slug": "podcast-slug",
      "urn": "urn:dr:radio:series:xxxxx"
    }
  ]
}
```

## Technical Details

- **Language**: C# (.NET 9.0)
- **Compilation**: NativeAOT for fast startup and small binary size
- **HTTP Client**: Configured with Polly retry policies
- **JSON Serialization**: Source-generated for trim compatibility
- **RSS Generation**: XDocument-based with Atom, iTunes, and Media RSS namespaces
- **Deployment**: Automated via GitHub Actions to GitHub Pages

## CI/CD Workflows

### Build and Release
Triggered on pushes to `main` or pull requests affecting `src/`:
- Determines semantic version from PR/commit messages
- Builds cross-platform NativeAOT binaries
- Creates GitHub releases (prereleases for PRs, stable for main)
- Cleans up prereleases after stable releases

### Feed Generation
Runs hourly and on changes to [podcasts.json](podcasts.json):
- Downloads latest binary from releases
- Generates RSS feeds for all configured podcasts
- Compares feeds with deployed versions (hash-based)
- Deploys to GitHub Pages only if changes detected

## License

This project is provided as-is for educational purposes.