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
This project currently has a test directory structure but no active test framework configured. When adding tests, they should be placed in the `tests/` directory.

## Architecture Overview

### Core Components
- **PodcastFeedGenerator.cs**: Main application entry point and RSS feed generation logic
- **PodcastModels.cs**: Data models with JSON source generation for NativeAOT compatibility
- **podcasts.json**: Configuration file containing podcast slugs and URNs to process

### Key Design Patterns
- **NativeAOT Optimization**: Uses source-generated JSON serialization, trim-safe patterns, and aggressive optimization settings
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
- **Compilation**: NativeAOT with aggressive trimming and optimization
- **Warning Policy**: Treats warnings as errors (except CS8618 for nullable reference types)
- **Globalization**: Invariant mode for smaller binary size