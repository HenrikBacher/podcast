# Azure Function Setup Guide

This guide walks you through setting up the GitHub environment and Azure Function App for automated deployment.

## Prerequisites

- Azure subscription (free tier works)
- GitHub repository admin access
- Azure CLI or Azure Portal access

---

## Step 1: Create Azure Function App

### Option A: Azure Portal (Recommended for beginners)

1. Go to [Azure Portal](https://portal.azure.com)
2. Click **Create a resource** → Search for **Function App**
3. Click **Create**

**Configuration:**
```
Basics:
  Subscription: [Your subscription]
  Resource Group: [Create new] "drpodcast-rg"
  Function App name: "drpodcast-feed-generator"
  Publish: Code
  Runtime stack: .NET
  Version: 9 (STS) Isolated
  Region: West Europe (or closest to Denmark)
  Operating System: Linux

Hosting:
  Storage account: [Create new or use existing]
  Plan type: Consumption (Serverless)

Networking:
  Enable public access: Yes

Monitoring:
  Application Insights: Yes
  Application Insights name: [Auto-generated]
```

4. Click **Review + create**
5. Click **Create** and wait for deployment (~2-3 minutes)

### Option B: Azure CLI

```bash
# Login to Azure
az login

# Create resource group
az group create --name drpodcast-rg --location westeurope

# Create storage account
az storage account create \
  --name drpodcaststorage \
  --resource-group drpodcast-rg \
  --location westeurope \
  --sku Standard_LRS

# Create Function App
az functionapp create \
  --resource-group drpodcast-rg \
  --consumption-plan-location westeurope \
  --runtime dotnet-isolated \
  --runtime-version 9 \
  --functions-version 4 \
  --name drpodcast-feed-generator \
  --storage-account drpodcaststorage \
  --os-type Linux
```

---

## Step 2: Configure Azure Function App Settings

### Application Settings (Environment Variables)

In Azure Portal:
1. Go to your Function App
2. Navigate to **Settings** → **Configuration**
3. Click **New application setting** for each:

| Name | Value | Description |
|------|-------|-------------|
| `API_KEY` | `your_dr_api_key_here` | DR API key for accessing podcast data |
| `BASE_URL` | `https://henrikbacher.github.io/podcast` | Base URL for generated feeds |

4. Click **Save** then **Continue** to restart the app

### Alternative: Azure CLI

```bash
az functionapp config appsettings set \
  --name drpodcast-feed-generator \
  --resource-group drpodcast-rg \
  --settings \
    API_KEY="your_dr_api_key_here" \
    BASE_URL="https://henrikbacher.github.io/podcast"
```

---

## Step 3: Get Publish Profile

### Azure Portal:

1. Go to your Function App
2. Click **Overview** tab
3. Click **Get publish profile** (download)
4. Open the downloaded file in a text editor
5. **Copy the entire XML content**

### Azure CLI:

```bash
az functionapp deployment list-publishing-profiles \
  --name drpodcast-feed-generator \
  --resource-group drpodcast-rg \
  --xml
```

Copy the output XML.

---

## Step 4: Create GitHub Environment

### Manual Steps:

1. Go to your GitHub repository
2. Navigate to **Settings** → **Environments**
3. Click **New environment**
4. Name: `azure-production`
5. Click **Configure environment**

### Environment Configuration:

**Protection Rules (Optional but Recommended):**
- ☑ Required reviewers: Add yourself
- ☑ Wait timer: 0 minutes
- ☑ Deployment branches: Selected branches → `main`

Click **Save protection rules**

---

## Step 5: Add Environment Variables

In the `azure-production` environment:

### Variables (Public)

Click **Add variable** for each:

| Name | Value | Example |
|------|-------|---------|
| `AZURE_FUNCTIONAPP_NAME` | Your function app name | `drpodcast-feed-generator` |
| `AZURE_FUNCTION_URL` | Your function app URL | `https://drpodcast-feed-generator.azurewebsites.net` |

**To find your Function App URL:**
- Azure Portal → Function App → **Overview** → **URL** field
- Or: `https://[your-app-name].azurewebsites.net`

---

## Step 6: Add Environment Secrets

In the `azure-production` environment:

### Secrets (Private)

Click **Add secret**:

| Name | Value |
|------|-------|
| `AZURE_FUNCTIONAPP_PUBLISH_PROFILE` | Paste the entire XML content from Step 3 |

**Important:** The publish profile is sensitive - treat it like a password!

---

## Step 7: Verify Setup

### Checklist:

- [ ] Azure Function App created
- [ ] Application settings configured (`API_KEY`, `BASE_URL`)
- [ ] GitHub environment `azure-production` created
- [ ] Environment variable `AZURE_FUNCTIONAPP_NAME` set
- [ ] Environment variable `AZURE_FUNCTION_URL` set
- [ ] Environment secret `AZURE_FUNCTIONAPP_PUBLISH_PROFILE` set

### Test Deployment:

1. Push to `main` branch or manually trigger workflow
2. Go to **Actions** tab in GitHub
3. Watch **Deploy Azure Function** workflow
4. Should complete in ~2-3 minutes

### Verify Function is Running:

```bash
# Health check (no auth required)
curl https://[your-app-name].azurewebsites.net/api/HealthCheck

# Expected response:
# {"status":"healthy","service":"DrPodcast Feed Generator"}
```

---

## Step 8: Test Feed Generation

### Get Function Key:

1. Azure Portal → Function App
2. **Functions** → **GenerateFeedsHttp**
3. **Function Keys** → **default** → Copy key

### Trigger Manually:

```bash
curl -X POST "https://[your-app-name].azurewebsites.net/api/GenerateFeedsHttp?code=[function-key]"
```

Or wait for the hourly timer trigger to run automatically.

---

## Monitoring & Troubleshooting

### View Logs:

**Azure Portal:**
1. Function App → **Monitoring** → **Log stream**
2. Or: **Application Insights** → **Logs**

**Query Example (Kusto/KQL):**
```kql
traces
| where timestamp > ago(1h)
| where message contains "Feed generation"
| order by timestamp desc
```

### Common Issues:

**Deployment fails:**
- Check publish profile is valid
- Verify .NET 9 runtime is selected
- Check workflow logs in GitHub Actions

**Function not running:**
- Check Application Settings are set
- Verify `API_KEY` is valid
- Check Application Insights for errors

**Timer not triggering:**
- Wait up to 5 minutes after deployment
- Check Function App → Functions → GenerateFeedsTimer → Monitor

---

## Cost Monitoring

### Free Tier Limits:
- **Executions**: 1,000,000 per month (you'll use ~720)
- **Compute**: 400,000 GB-s per month (you'll use ~6,000)
- **Storage**: 5 GB

### Monitor Costs:
1. Azure Portal → **Cost Management + Billing**
2. Set up budget alerts (optional)

**Expected Cost:** $0.00/month (well within free tier)

---

## Security Best Practices

1. **Rotate publish profile every 90 days:**
   - Download new profile
   - Update GitHub secret

2. **Use managed identities (advanced):**
   - Instead of publish profiles
   - More secure for production

3. **Enable authentication:**
   - Function App → **Authentication** → **Add identity provider**
   - Protect HTTP trigger endpoints

4. **Monitor access:**
   - Check Application Insights regularly
   - Set up alerts for failures

---

## Quick Reference

### URLs:
- **Function App**: `https://[app-name].azurewebsites.net`
- **Health Check**: `https://[app-name].azurewebsites.net/api/HealthCheck`
- **Manual Trigger**: `https://[app-name].azurewebsites.net/api/GenerateFeedsHttp`

### GitHub Environment:
- **Name**: `azure-production`
- **Variables**: `AZURE_FUNCTIONAPP_NAME`, `AZURE_FUNCTION_URL`
- **Secrets**: `AZURE_FUNCTIONAPP_PUBLISH_PROFILE`

### Azure Resources:
- **Resource Group**: `drpodcast-rg`
- **Function App**: `drpodcast-feed-generator`
- **Plan**: Consumption (Serverless)
- **Runtime**: .NET 9 Isolated

---

## Next Steps

1. **Monitor first few runs** - Check logs for any issues
2. **Set up alerts** - Application Insights → Alerts
3. **Document API key rotation** - Create calendar reminder
4. **Consider adding**:
   - Blob storage for feed output
   - CDN for feed distribution
   - Custom domain

---

## Support

- **Azure Documentation**: https://docs.microsoft.com/azure/azure-functions/
- **GitHub Actions**: https://docs.github.com/actions
- **.NET 9 Functions**: https://aka.ms/dotnet-isolated-process

For issues with this setup, open a GitHub issue in the repository.
