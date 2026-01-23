# DrPodcast

Automated podcast RSS feed generator for DR (Danmarks Radio). Generates iTunes-compatible RSS feeds for **41 Danish podcasts** with cross-platform NativeAOT binaries and GitHub Pages deployment.

## Quick Start

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- DR API key

### Run from Source
```bash
dotnet restore src/DrPodcast.csproj
dotnet build src/DrPodcast.csproj -c Release

export API_KEY="your-dr-api-key"
export BASE_URL="https://your-domain.com"
dotnet run --project src/DrPodcast.csproj
```

### Pre-built Binaries
Download from [Releases](../../releases):
- `DrPodcast-linux-x64` / `DrPodcast-linux-arm64`
- `DrPodcast-win-x64.exe`
- `DrPodcast-osx-arm64`

```bash
API_KEY=<key> BASE_URL=<url> ./DrPodcast-linux-x64
```

## Configuration

### Environment Variables
| Variable | Required | Description |
|----------|----------|-------------|
| `API_KEY` | Yes | DR API key |
| `BASE_URL` | No | Base URL for feeds (default: `https://example.com`) |

### Adding Podcasts
Edit [podcasts.json](podcasts.json):
```json
{
  "podcasts": [
    { "slug": "example-podcast", "urn": "urn:dr:radio:series:..." }
  ]
}
```

## Testing
```bash
dotnet test tests/DrPodcast.Tests/DrPodcast.Tests.csproj
```

## Project Structure

```
src/
  PodcastFeedGenerator.cs   # Main entry point and RSS generation
  PodcastModels.cs          # Data models with JSON source generation
  PodcastHelpers.cs         # Image URL extraction and category mapping
  WebsiteGenerator.cs       # Static website and manifest generation
tests/                      # xUnit test suite
site/                       # Static website for feed browsing
.github/workflows/
  build-and-release.yml     # Cross-platform builds and releases
  generate-feed.yml         # Hourly feed generation and Pages deployment
```

## Technical Details

- .NET 10.0 with NativeAOT compilation
- Polly retry policies for resilient HTTP
- RSS 2.0 with iTunes, Atom, and Media RSS namespaces
- Source-generated JSON serialization (trim-safe)
- Cross-platform: Linux x64/ARM64, Windows x64, macOS ARM64

## License

This project is provided as-is for educational purposes.
