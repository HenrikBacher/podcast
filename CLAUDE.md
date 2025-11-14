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

### Project Structure

The repository contains two implementations:

1. **Console Application** (`src/`): Original NativeAOT-compiled binary for local/GitHub Actions execution
2. **Azure Functions** (`src-functions/`): Serverless implementation for cloud deployment
3. **Shared Library** (`src-shared/`): Common business logic used by both implementations

### Core Components

**Shared Library** (`src-shared/`):
- **FeedGeneratorService.cs**: Core feed generation orchestration
- **PodcastModels.cs**: Data models with JSON source generation for NativeAOT compatibility
- **PodcastHelpers.cs**: Helper functions for category mapping and image URL extraction
- **WebsiteGenerator.cs**: Static website generation (index.html, manifest.json)

**Console Application** (`src/`):
- **PodcastFeedGenerator.cs**: Main application entry point and RSS feed generation logic

**Azure Functions** (`src-functions/`):
- **FeedGeneratorFunction.cs**: Timer-triggered (hourly) and HTTP-triggered functions
- **BlobStorageService.cs**: Uploads generated content to Azure Blob Storage
- **Program.cs**: Azure Functions host configuration

**Configuration**:
- **podcasts.json**: Configuration file containing podcast slugs and URNs to process (34 podcasts)

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

**Console App / GitHub Actions**:
- `API_KEY`: DR API key (required)
- `BASE_URL`: Base URL for deployed feeds (default: "https://example.com")

**Azure Functions** (additional):
- `AZURE_STORAGE_CONNECTION_STRING`: Azure Storage connection string for blob upload
- `STORAGE_CONTAINER_NAME`: Blob container name (default: "$web" for static websites)
- `APPLICATIONINSIGHTS_CONNECTION_STRING`: Optional Application Insights connection

### Project Configuration

**Console App** (`src/DrPodcast.csproj`):
- **Target Framework**: .NET 10.0
- **Compilation**: NativeAOT with aggressive trimming and optimization
- **Warning Policy**: Treats warnings as errors (except CS8618 for nullable reference types)
- **Globalization**: Invariant mode for smaller binary size

**Azure Functions** (`src-functions/DrPodcast.Functions.csproj`):
- **Target Framework**: .NET 10.0
- **Runtime**: Isolated worker model (dotnet-isolated)
- **Azure Functions Version**: v4
- **Dependencies**: Azure.Storage.Blobs, Microsoft.Azure.Functions.Worker

**Shared Library** (`src-shared/DrPodcast.Shared.csproj`):
- **Target Framework**: .NET 10.0
- **Type**: Class library
- **Dependencies**: Microsoft.Extensions.Http, Polly

## Azure Functions Development

### Build Azure Functions
```bash
# Restore and build shared library first
dotnet restore src-shared/DrPodcast.Shared.csproj
dotnet build src-shared/DrPodcast.Shared.csproj --configuration Release

# Restore and build Azure Functions
dotnet restore src-functions/DrPodcast.Functions.csproj
dotnet build src-functions/DrPodcast.Functions.csproj --configuration Release
```

### Run Azure Functions Locally
```bash
cd src-functions
func start
```

**Prerequisites**: Azure Functions Core Tools v4 (`npm install -g azure-functions-core-tools@4`)

### Test Manual Trigger
```bash
curl -X POST http://localhost:7071/api/FeedGeneratorManual
```

### Deploy to Azure
See [AZURE_SETUP.md](AZURE_SETUP.md) for comprehensive deployment instructions.

### Azure Functions Features
- **Timer Trigger**: Runs every hour (`0 0 * * * *` cron expression)
- **HTTP Trigger**: Manual execution endpoint for testing
- **Blob Storage Upload**: Automatically uploads generated feeds and website to Azure Blob Storage
- **Custom Domain Support**: Works with Azure CDN for custom domains and HTTPS

## Deployment Options

### Option 1: GitHub Pages (Original)
- **Workflow**: `.github/workflows/generate-feed.yml`
- **Trigger**: Hourly cron job
- **Deployment**: GitHub Pages via `actions/deploy-pages@v4`
- **Cost**: Free

### Option 2: Azure Functions (New)
- **Workflow**: `.github/workflows/azure-deploy.yml`
- **Trigger**: Push to `azure-functions-migration` branch
- **Deployment**: Azure Functions with Blob Storage
- **Cost**: ~$2-3/month (or free tier)
- **Documentation**: See [AZURE_SETUP.md](AZURE_SETUP.md) and [AZURE_README.md](AZURE_README.md)