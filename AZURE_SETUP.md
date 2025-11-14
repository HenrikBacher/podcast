# Azure Functions Setup Guide

This guide explains how to deploy DrPodcast as an Azure Function with Azure Blob Storage (Static Website) hosting.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│ Azure Functions (Timer Trigger - Hourly)               │
│  • Fetch podcast data from DR API                      │
│  • Generate RSS feeds (34 feeds)                       │
│  • Generate website (index.html, CSS, JS)              │
│  • Upload to Azure Blob Storage                        │
└─────────────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────────────┐
│ Azure Blob Storage (Static Website)                    │
│  • Container: $web                                      │
│  • Content: feeds/*.xml, index.html, etc.              │
│  • Public access for podcast clients                   │
└─────────────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────────────┐
│ Azure CDN (Optional)                                    │
│  • Custom domain mapping                                │
│  • Global caching                                       │
│  • HTTPS with custom certificate                       │
└─────────────────────────────────────────────────────────┘
```

## Prerequisites

1. **Azure Subscription** - [Sign up for free](https://azure.microsoft.com/free/)
2. **Azure CLI** - [Install Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli)
3. **.NET 10 SDK** - [Download .NET 10](https://dotnet.microsoft.com/download)
4. **DR API Key** - API key for Danmarks Radio API

## Step 1: Create Azure Resources

### 1.1 Login to Azure

```bash
az login
```

### 1.2 Create Resource Group

```bash
az group create \
  --name drpodcast-rg \
  --location westeurope
```

### 1.3 Create Storage Account

```bash
az storage account create \
  --name drpodcaststorage \
  --resource-group drpodcast-rg \
  --location westeurope \
  --sku Standard_LRS \
  --kind StorageV2
```

### 1.4 Enable Static Website Hosting

```bash
az storage blob service-properties update \
  --account-name drpodcaststorage \
  --static-website \
  --index-document index.html \
  --404-document index.html
```

### 1.5 Get Storage Connection String

```bash
az storage account show-connection-string \
  --name drpodcaststorage \
  --resource-group drpodcast-rg \
  --query connectionString \
  --output tsv
```

**Save this connection string** - you'll need it for environment variables.

### 1.6 Create Function App

```bash
az functionapp create \
  --name drpodcast-functions \
  --resource-group drpodcast-rg \
  --consumption-plan-location westeurope \
  --runtime dotnet-isolated \
  --runtime-version 10 \
  --functions-version 4 \
  --storage-account drpodcaststorage \
  --os-type Linux
```

## Step 2: Configure Environment Variables

Set the required environment variables in Azure Function App:

```bash
az functionapp config appsettings set \
  --name drpodcast-functions \
  --resource-group drpodcast-rg \
  --settings \
    "API_KEY=your-dr-api-key" \
    "BASE_URL=https://your-custom-domain.com" \
    "AZURE_STORAGE_CONNECTION_STRING=your-connection-string" \
    "STORAGE_CONTAINER_NAME=\$web"
```

**Required Variables:**
- `API_KEY` - Your DR API key
- `BASE_URL` - Your custom domain URL (e.g., `https://podcast.example.com`)
- `AZURE_STORAGE_CONNECTION_STRING` - Connection string from Step 1.5
- `STORAGE_CONTAINER_NAME` - Use `$web` for static website hosting

## Step 3: Setup GitHub Actions Deployment

### 3.1 Get Publish Profile

```bash
az functionapp deployment list-publishing-profiles \
  --name drpodcast-functions \
  --resource-group drpodcast-rg \
  --xml
```

### 3.2 Add GitHub Secret

1. Go to your GitHub repository
2. Navigate to **Settings** > **Secrets and variables** > **Actions**
3. Click **New repository secret**
4. Name: `AZURE_FUNCTIONAPP_PUBLISH_PROFILE`
5. Value: Paste the XML output from step 3.1

### 3.3 Trigger Deployment

Push changes to the `azure-functions-migration` branch:

```bash
git push origin azure-functions-migration
```

GitHub Actions will automatically build and deploy the function.

## Step 4: Configure Custom Domain

### 4.1 Get Static Website URL

```bash
az storage account show \
  --name drpodcaststorage \
  --resource-group drpodcast-rg \
  --query "primaryEndpoints.web" \
  --output tsv
```

This will output something like: `https://drpodcaststorage.z6.web.core.windows.net/`

### 4.2 Option A: Direct DNS (Simple, no CDN)

Update your DNS records:

```
Type: CNAME
Name: podcast (or your subdomain)
Value: drpodcaststorage.z6.web.core.windows.net
```

**Note:** Azure Blob Storage static websites don't support custom domains with HTTPS directly. Use Azure CDN (Option B) for HTTPS.

### 4.2 Option B: Azure CDN (Recommended for HTTPS)

#### Create CDN Profile

```bash
az cdn profile create \
  --name drpodcast-cdn \
  --resource-group drpodcast-rg \
  --sku Standard_Microsoft
```

#### Create CDN Endpoint

```bash
# Get the static website hostname (without https://)
ORIGIN=$(az storage account show \
  --name drpodcaststorage \
  --resource-group drpodcast-rg \
  --query "primaryEndpoints.web" \
  --output tsv | sed 's|https://||' | sed 's|/$||')

az cdn endpoint create \
  --name drpodcast-cdn-endpoint \
  --profile-name drpodcast-cdn \
  --resource-group drpodcast-rg \
  --origin $ORIGIN \
  --origin-host-header $ORIGIN
```

#### Add Custom Domain

```bash
az cdn custom-domain create \
  --endpoint-name drpodcast-cdn-endpoint \
  --profile-name drpodcast-cdn \
  --resource-group drpodcast-rg \
  --name podcast-example-com \
  --hostname podcast.example.com
```

#### Enable HTTPS

```bash
az cdn custom-domain enable-https \
  --endpoint-name drpodcast-cdn-endpoint \
  --profile-name drpodcast-cdn \
  --resource-group drpodcast-rg \
  --name podcast-example-com
```

#### Update DNS

```
Type: CNAME
Name: podcast
Value: drpodcast-cdn-endpoint.azureedge.net
```

## Step 5: Verify Deployment

### 5.1 Check Function Logs

```bash
az functionapp log tail \
  --name drpodcast-functions \
  --resource-group drpodcast-rg
```

### 5.2 Manual Trigger (Testing)

Trigger the function manually via HTTP endpoint:

```bash
FUNCTION_KEY=$(az functionapp function keys list \
  --name drpodcast-functions \
  --resource-group drpodcast-rg \
  --function-name FeedGeneratorManual \
  --query default \
  --output tsv)

curl -X POST "https://drpodcast-functions.azurewebsites.net/api/FeedGeneratorManual?code=$FUNCTION_KEY"
```

### 5.3 Verify Feed Generation

Visit your custom domain:

```
https://podcast.example.com/index.html
https://podcast.example.com/feeds/genstart.xml
https://podcast.example.com/manifest.json
```

## Timer Schedule

The function runs automatically on this schedule:

- **Cron Expression:** `0 0 * * * *`
- **Frequency:** Every hour (at the top of the hour)
- **Timezone:** UTC

To modify the schedule, edit `src-functions/FeedGeneratorFunction.cs`:

```csharp
[TimerTrigger("0 0 * * * *")] // Change this cron expression
```

## Cost Estimation

Based on typical usage (34 feeds, hourly updates):

| Service | Usage | Est. Cost/Month |
|---------|-------|----------------|
| Azure Functions (Consumption) | ~730 executions, ~2 min each | $0.50 - $1.00 |
| Blob Storage | ~50 MB storage, minimal transactions | $0.10 - $0.20 |
| Bandwidth (egress) | ~10 GB/month (podcast feeds) | $0.50 - $1.00 |
| Azure CDN (optional) | ~10 GB bandwidth | $0.60 - $1.20 |
| **Total** | | **$1.70 - $3.40** |

**Free Tier Benefits:**
- Azure Functions: First 1 million executions free
- Blob Storage: First 5 GB free
- You'll likely stay within free tier limits!

## Monitoring & Debugging

### View Execution History

```bash
az functionapp function show \
  --name drpodcast-functions \
  --resource-group drpodcast-rg \
  --function-name FeedGeneratorTimer
```

### Enable Application Insights

```bash
az monitor app-insights component create \
  --app drpodcast-insights \
  --resource-group drpodcast-rg \
  --location westeurope

INSTRUMENTATION_KEY=$(az monitor app-insights component show \
  --app drpodcast-insights \
  --resource-group drpodcast-rg \
  --query instrumentationKey \
  --output tsv)

az functionapp config appsettings set \
  --name drpodcast-functions \
  --resource-group drpodcast-rg \
  --settings "APPLICATIONINSIGHTS_CONNECTION_STRING=InstrumentationKey=$INSTRUMENTATION_KEY"
```

### View Logs in Azure Portal

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Resource Groups** > **drpodcast-rg**
3. Click on **drpodcast-functions**
4. Select **Monitor** > **Logs**

## Local Development

### Install Azure Functions Core Tools

```bash
npm install -g azure-functions-core-tools@4
```

### Configure Local Settings

Edit `src-functions/local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "API_KEY": "your-dr-api-key",
    "BASE_URL": "http://localhost:7071",
    "AZURE_STORAGE_CONNECTION_STRING": "your-storage-connection-string",
    "STORAGE_CONTAINER_NAME": "$web"
  }
}
```

### Run Locally

```bash
cd src-functions
dotnet build
func start
```

### Test Manual Trigger

```bash
curl -X POST http://localhost:7071/api/FeedGeneratorManual
```

## Troubleshooting

### Function Not Triggering

Check timer status:
```bash
az functionapp function show \
  --name drpodcast-functions \
  --resource-group drpodcast-rg \
  --function-name FeedGeneratorTimer
```

### Storage Upload Failing

Verify connection string:
```bash
az functionapp config appsettings list \
  --name drpodcast-functions \
  --resource-group drpodcast-rg \
  --query "[?name=='AZURE_STORAGE_CONNECTION_STRING']"
```

### Feeds Not Updating

Check blob container:
```bash
az storage blob list \
  --account-name drpodcaststorage \
  --container-name '$web' \
  --output table
```

## Migration from GitHub Pages

### DNS Cutover Checklist

1. ✅ Deploy Azure Function and verify feeds are generating
2. ✅ Verify all 34 feeds are accessible via Azure static website
3. ✅ Test one feed in a podcast client (e.g., Apple Podcasts)
4. ✅ Update DNS CNAME to point to Azure CDN endpoint
5. ✅ Wait for DNS propagation (15-60 minutes)
6. ✅ Enable HTTPS on custom domain
7. ✅ Verify all feeds are accessible via custom domain with HTTPS
8. ✅ Monitor for 24 hours to ensure automatic updates work

### Rollback Plan

If issues occur:

1. Revert DNS CNAME to GitHub Pages: `username.github.io`
2. Wait for DNS propagation
3. Investigate Azure Function logs
4. Fix issues and retry

## Additional Resources

- [Azure Functions Documentation](https://docs.microsoft.com/azure/azure-functions/)
- [Azure Blob Storage Static Websites](https://docs.microsoft.com/azure/storage/blobs/storage-blob-static-website)
- [Azure CDN Documentation](https://docs.microsoft.com/azure/cdn/)
- [Timer Trigger for Azure Functions](https://docs.microsoft.com/azure/azure-functions/functions-bindings-timer)

## Support

For issues specific to this implementation:
- Check GitHub Issues: [github.com/your-username/podcast/issues](https://github.com/your-username/podcast/issues)
- Review Azure Function logs via Azure Portal
- Verify environment variables are set correctly
