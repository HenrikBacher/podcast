# DrPodcast - Azure Functions Edition

This directory contains the Azure Functions implementation of DrPodcast, designed to run as a serverless application with automatic feed generation and deployment to Azure Blob Storage.

## Project Structure

```
podcast/
├── src/                          # Original console application
│   ├── DrPodcast.csproj
│   ├── PodcastFeedGenerator.cs
│   └── ...
│
├── src-shared/                   # Shared library (reusable logic)
│   ├── DrPodcast.Shared.csproj
│   ├── PodcastModels.cs          # Data models with JSON serialization
│   ├── PodcastHelpers.cs         # Helper functions
│   ├── WebsiteGenerator.cs       # Website generation logic
│   ├── FeedGeneratorService.cs   # Core feed generation service
│   └── GlobalUsings.cs
│
├── src-functions/                # Azure Functions application
│   ├── DrPodcast.Functions.csproj
│   ├── Program.cs                # Function app entry point
│   ├── FeedGeneratorFunction.cs  # Timer & HTTP triggered functions
│   ├── BlobStorageService.cs     # Azure Blob Storage upload
│   ├── IStorageService.cs        # Storage abstraction
│   ├── host.json                 # Function app configuration
│   └── local.settings.json       # Local development settings (gitignored)
│
├── tests/                        # Test suite
│   └── DrPodcast.Tests/
│
├── .github/workflows/
│   ├── build-and-release.yml     # Original release workflow
│   ├── generate-feed.yml         # GitHub Pages deployment
│   └── azure-deploy.yml          # Azure Functions deployment (NEW)
│
├── podcasts.json                 # Podcast configuration (34 podcasts)
├── site/                         # Static website templates
├── AZURE_SETUP.md               # Comprehensive Azure setup guide (NEW)
└── README.md                     # Original project documentation
```

## Key Features

### Azure Functions

- **Timer Trigger**: Automatically runs every hour (`0 0 * * * *` cron)
- **HTTP Trigger**: Manual execution endpoint for testing
- **Isolated Worker**: Uses .NET 10 isolated worker model
- **Resilient**: Polly retry policies for DR API calls

### Deployment

- **Azure Blob Storage**: Hosts static website ($web container)
- **GitHub Actions**: Automated CI/CD pipeline
- **Custom Domain**: Support for custom domain with Azure CDN
- **HTTPS**: Automatic HTTPS via Azure CDN

### Architecture Benefits

1. **Serverless**: No infrastructure management, auto-scaling
2. **Cost-Effective**: ~$2-3/month (or free tier)
3. **Reliable**: Built-in retry logic, monitoring with App Insights
4. **URL Preservation**: Same URLs as GitHub Pages with custom domain

## Quick Start

### Prerequisites

- Azure subscription
- Azure CLI installed
- .NET 10 SDK
- DR API key

### 1. Build the Project

```bash
# Restore and build shared library
dotnet restore src-shared/DrPodcast.Shared.csproj
dotnet build src-shared/DrPodcast.Shared.csproj

# Restore and build Azure Functions
dotnet restore src-functions/DrPodcast.Functions.csproj
dotnet build src-functions/DrPodcast.Functions.csproj
```

### 2. Configure Local Development

Copy and edit `src-functions/local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "API_KEY": "your-dr-api-key",
    "BASE_URL": "http://localhost:7071",
    "AZURE_STORAGE_CONNECTION_STRING": "DefaultEndpointsProtocol=https;...",
    "STORAGE_CONTAINER_NAME": "$web"
  }
}
```

### 3. Run Locally

```bash
cd src-functions
func start
```

### 4. Test Manual Trigger

```bash
curl -X POST http://localhost:7071/api/FeedGeneratorManual
```

### 5. Deploy to Azure

See [AZURE_SETUP.md](AZURE_SETUP.md) for complete deployment instructions.

## Environment Variables

### Required

| Variable | Description | Example |
|----------|-------------|---------|
| `API_KEY` | DR API key for fetching podcast data | `abc123xyz...` |
| `BASE_URL` | Base URL for feed self-references | `https://podcast.example.com` |
| `AZURE_STORAGE_CONNECTION_STRING` | Azure Storage connection string | `DefaultEndpointsProtocol=https;...` |

### Optional

| Variable | Description | Default |
|----------|-------------|---------|
| `STORAGE_CONTAINER_NAME` | Blob container name | `$web` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | App Insights for monitoring | None |

## Functions

### FeedGeneratorTimer

- **Trigger**: Timer (cron: `0 0 * * * *`)
- **Schedule**: Every hour, on the hour
- **Actions**:
  1. Fetch podcast metadata from DR API
  2. Generate 34 RSS feeds
  3. Generate website (index.html, CSS, JS)
  4. Upload to Azure Blob Storage

### FeedGeneratorManual

- **Trigger**: HTTP POST
- **Authentication**: Function-level (requires function key)
- **Endpoint**: `/api/FeedGeneratorManual`
- **Use Case**: Manual testing, emergency updates

## Testing

Run the existing test suite:

```bash
dotnet test tests/DrPodcast.Tests/DrPodcast.Tests.csproj
```

Tests cover:
- JSON serialization/deserialization
- Feed generation logic
- Category mapping
- Image URL extraction

## Monitoring

### View Logs (Azure)

```bash
az functionapp log tail \
  --name drpodcast-functions \
  --resource-group drpodcast-rg
```

### View Logs (Azure Portal)

1. Go to Azure Portal
2. Navigate to Function App
3. Click **Monitor** > **Live Metrics** or **Logs**

### Application Insights

Enable for advanced monitoring:

```bash
az monitor app-insights component create \
  --app drpodcast-insights \
  --resource-group drpodcast-rg \
  --location westeurope
```

## Comparison: Console App vs Azure Functions

| Feature | Console App (src/) | Azure Functions (src-functions/) |
|---------|-------------------|----------------------------------|
| Execution | Manual or GitHub Actions | Automatic (timer) or manual (HTTP) |
| Hosting | GitHub Pages | Azure Blob Storage + CDN |
| Deployment | GitHub Actions workflow | Azure Functions runtime |
| Scaling | N/A (single execution) | Auto-scaling (serverless) |
| Cost | Free (GitHub) | ~$2-3/month (Azure) |
| Custom Domain | Yes (via DNS) | Yes (via Azure CDN) |
| HTTPS | Automatic (GitHub Pages) | Automatic (Azure CDN) |
| Monitoring | GitHub Actions logs | App Insights + Azure Monitor |

## Shared Library

The `src-shared/` directory contains all the core business logic:

- **PodcastModels.cs**: Data models (Series, Episode, etc.)
- **PodcastHelpers.cs**: Utility functions
- **WebsiteGenerator.cs**: Static site generation
- **FeedGeneratorService.cs**: Feed generation orchestration

This design allows:
- Code reuse between console app and Azure Functions
- Easy testing without Azure dependencies
- Potential for future CLI tool or additional hosting options

## CI/CD Pipeline

### GitHub Actions Workflow

`.github/workflows/azure-deploy.yml` handles:

1. **Build**: Compile .NET projects
2. **Test**: Run xUnit test suite (optional)
3. **Publish**: Create Azure Functions deployment package
4. **Deploy**: Upload to Azure Functions using publish profile

### Secrets Required

| Secret | Description | How to Get |
|--------|-------------|-----------|
| `AZURE_FUNCTIONAPP_PUBLISH_PROFILE` | Deployment credentials | `az functionapp deployment list-publishing-profiles` |

## Troubleshooting

### Function Not Running

Check timer status:
```bash
az functionapp function show \
  --name drpodcast-functions \
  --resource-group drpodcast-rg \
  --function-name FeedGeneratorTimer
```

### Upload Failing

Verify storage connection:
```bash
# Test connection locally
az storage blob list \
  --account-name drpodcaststorage \
  --container-name '$web' \
  --connection-string "your-connection-string"
```

### Feeds Not Updating

1. Check function execution history in Azure Portal
2. Review logs for errors
3. Verify DR API key is valid
4. Confirm storage container exists

## Cost Optimization Tips

1. **Use Consumption Plan**: Pay only for executions (included in free tier)
2. **Optimize Timer**: Reduce frequency if hourly updates aren't necessary
3. **Enable CDN Caching**: Reduce blob storage egress costs
4. **Monitor Usage**: Set up cost alerts in Azure Portal

## Migration Path

To migrate from GitHub Pages:

1. Deploy Azure Functions (see AZURE_SETUP.md)
2. Verify feeds are generating correctly
3. Test with podcast clients
4. Update DNS CNAME to Azure CDN endpoint
5. Enable HTTPS
6. Monitor for 24-48 hours
7. Deprecate GitHub Pages deployment (optional)

## Development Workflow

### Adding a New Podcast

1. Edit `podcasts.json`:
   ```json
   {
     "slug": "new-podcast",
     "urn": "urn:dr:radio:series:xxxxx"
   }
   ```

2. Test locally:
   ```bash
   cd src-functions
   func start
   curl -X POST http://localhost:7071/api/FeedGeneratorManual
   ```

3. Commit and push:
   ```bash
   git add podcasts.json
   git commit -m "Add new podcast: new-podcast"
   git push
   ```

4. Verify in Azure after deployment

### Modifying Feed Generation Logic

1. Edit `src-shared/FeedGeneratorService.cs`
2. Run tests: `dotnet test`
3. Test locally with Azure Functions
4. Deploy via GitHub Actions

## Future Enhancements

Potential improvements:

- [ ] Add change detection (only upload modified files)
- [ ] Implement parallel blob uploads for faster deployment
- [ ] Add webhook endpoint for on-demand feed refresh
- [ ] Create dashboard for feed statistics
- [ ] Add support for podcast analytics
- [ ] Implement feed validation before upload

## Contributing

When contributing to the Azure Functions version:

1. Make changes in `src-shared/` for business logic
2. Update `src-functions/` for Azure-specific functionality
3. Add tests in `tests/DrPodcast.Tests/`
4. Update documentation (this file and AZURE_SETUP.md)
5. Test locally before pushing
6. Create pull request targeting `azure-functions-migration` branch

## License

Same as the main project (see root README.md).

## Support & Resources

- **Azure Setup Guide**: [AZURE_SETUP.md](AZURE_SETUP.md)
- **Original README**: [README.md](../README.md)
- **Azure Functions Docs**: https://docs.microsoft.com/azure/azure-functions/
- **GitHub Issues**: https://github.com/your-username/podcast/issues
