# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Commands

### Azure Functions (Primary)

The project is designed to run as an Azure Function with timer triggers for scheduled feed generation.

```bash
# Restore dependencies
dotnet restore src/DrPodcast.csproj

# Build the project
dotnet build src/DrPodcast.csproj --configuration Release

# Run Azure Functions locally (requires Azure Functions Core Tools)
cd src
func start

# Or use .NET directly
dotnet run --project src/DrPodcast.csproj
```

**Prerequisites:**
- Azure Functions Core Tools v4: `npm install -g azure-functions-core-tools@4`
- .NET 9.0 SDK

**Local Development:**
1. Copy `src/local.settings.json` and add your `API_KEY`
2. Run `func start` in the `src` directory
3. Functions will be available at `http://localhost:7071`

**Available Endpoints:**
- `GET|POST /api/GenerateFeedsHttp` - Manual feed generation
- `GET /api/HealthCheck` - Health check endpoint
- Timer trigger runs hourly (configured in code: `0 0 * * * *`)

### Legacy CLI Mode

For standalone execution without Azure Functions:

```bash
# Run as console application (legacy mode)
dotnet run --project src/DrPodcast.csproj

# Publish as NativeAOT binary (not compatible with Azure Functions)
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
- **Program.cs**: Azure Functions host configuration and dependency injection setup
- **FeedGenerationFunction.cs**: Azure Functions entry points (timer trigger, HTTP trigger, health check)
- **PodcastFeedService.cs**: Core feed generation logic encapsulated as an injectable service
- **PodcastModels.cs**: Data models with JSON source generation
- **PodcastHelpers.cs**: Helper functions for category mapping and image URL extraction
- **podcasts.json**: Configuration file containing podcast slugs and URNs to process
- **host.json**: Azure Functions configuration (timeouts, logging, etc.)
- **local.settings.json**: Local development settings (not committed to git)

### Key Design Patterns
- **Azure Functions (Isolated Worker)**: Uses .NET 9 isolated worker process model for better performance
- **Dependency Injection**: Services registered in Program.cs and injected into functions
- **Timer Triggers**: Hourly scheduled execution using cron expressions (`0 0 * * * *`)
- **HTTP Triggers**: Manual execution endpoint for testing and on-demand generation
- **Resilient HTTP**: HttpClient configured with Polly retry policies for reliable API calls
- **RSS Standards Compliance**: Generates feeds with iTunes, Atom, and Media RSS namespaces
- **Pagination Handling**: Automatically fetches all episodes across multiple API pages
- **Structured Logging**: Uses ILogger for Application Insights integration

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
- **Azure Functions Version**: v4 (isolated worker process)
- **Warning Policy**: Treats warnings as errors (except CS8618 for nullable reference types)
- **Dependencies**:
  - Microsoft.Azure.Functions.Worker
  - Microsoft.Azure.Functions.Worker.Extensions.Timer
  - Microsoft.Azure.Functions.Worker.Extensions.Http
  - Microsoft.Extensions.Http.Polly (for resilient HTTP calls)
  - Microsoft.ApplicationInsights (for telemetry)

### Deployment
- **Azure**: Deployed via GitHub Actions using Azure Functions Action
- **Secrets Required**:
  - `AZURE_FUNCTIONAPP_PUBLISH_PROFILE`: Download from Azure Portal
  - Azure Function App must be configured with `API_KEY` and `BASE_URL` environment variables
- **Workflow**: `.github/workflows/deploy-azure-function.yml`