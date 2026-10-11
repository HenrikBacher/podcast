# DrPodcast

Automated podcast RSS feed generator for DR (Danmarks Radio). Generates iTunes-compatible RSS feeds for **every podcast series DR lists** (about 650 with downloadable audio) as a self-hosted web server with periodic background refresh.

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

### Docker
```bash
docker pull ghcr.io/YOUR_ORG/drpodcast:latest
docker run -e API_KEY=<key> -e BASE_URL=<url> -p 8080:8080 ghcr.io/YOUR_ORG/drpodcast:latest
```

## Configuration

### Environment Variables
| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `API_KEY` | Yes | — | DR API key |
| `BASE_URL` | No | `https://example.com` | Base URL for feed URLs in RSS output |
| `REFRESH_INTERVAL_MINUTES` | No | `15` | How often feeds are regenerated in the background (clamped to 1–1440) |

### Which Podcasts
Every series DR lists is published; new series appear by themselves. A feed's URL is
`{BASE_URL}/feeds/{slug}.xml`, where the slug is DR's own series slug without its numeric suffix.

## Testing
```bash
dotnet test tests/DrPodcast.Tests/DrPodcast.Tests.csproj
```

## Project Structure

```
src/
  PodcastFeedGenerator.cs      # Entry point: HTTP server
  FeedGenerationService.cs     # RSS feed generation and DR API integration
  FeedRefreshBackgroundService.cs  # Periodic background refresh with backoff
  PodcastModels.cs             # Data models with source-generated JSON serialization
  PodcastHelpers.cs            # Image URL extraction
  WebsiteGenerator.cs          # Static website and manifest generation
  PodcastCatalog.cs            # Which podcasts to publish, discovered from DR
tests/                         # xUnit test suite
site/                          # Static website assets for feed browsing
.github/workflows/
  build-and-release.yml        # Docker build, versioning, and GitHub releases
```

## Technical Details

- .NET 10.0 with NativeAOT compilation (trim-safe, invariant globalization)
- Polly retry policies for resilient HTTP against the DR API
- RSS 2.0 with iTunes and Atom namespaces
- Source-generated JSON serialization
- Atomic feed writes (temp file → rename) to avoid serving partial content
- Change detection via `<lastBuildDate>` comparison to skip unnecessary regenerations

## License

This project is provided as-is for educational purposes.
