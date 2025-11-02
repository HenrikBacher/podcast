# GitHub Environment Setup - Quick Start

This is a quick reference for setting up the `azure-production` GitHub environment.

## Create Environment

1. **Go to:** Repository → Settings → Environments
2. **Click:** New environment
3. **Name:** `azure-production`
4. **Click:** Configure environment

---

## Add Environment Variables

Click **Add variable** for each:

### Required Variables

| Variable Name | Example Value | Where to Get It |
|---------------|---------------|-----------------|
| `AZURE_FUNCTIONAPP_NAME` | `drpodcast-feed-generator` | Azure Portal → Function App name |
| `AZURE_FUNCTION_URL` | `https://drpodcast-feed-generator.azurewebsites.net` | Azure Portal → Function App → Overview → URL |

**How to find values:**

```bash
# Get Function App URL using Azure CLI
az functionapp show \
  --name drpodcast-feed-generator \
  --resource-group drpodcast-rg \
  --query defaultHostName \
  --output tsv
# Output: drpodcast-feed-generator.azurewebsites.net
# Add https:// prefix
```

---

## Add Environment Secrets

Click **Add secret**:

### Required Secrets

| Secret Name | Where to Get It |
|-------------|-----------------|
| `AZURE_FUNCTIONAPP_PUBLISH_PROFILE` | Azure Portal → Function App → Get publish profile (download) |

**Steps to get publish profile:**

1. Azure Portal → Your Function App
2. Overview tab
3. Click **Get publish profile** button (downloads XML file)
4. Open the file in text editor
5. Copy **entire contents** (all XML)
6. Paste into GitHub secret value

**Or via Azure CLI:**
```bash
az functionapp deployment list-publishing-profiles \
  --name drpodcast-feed-generator \
  --resource-group drpodcast-rg \
  --xml > publish-profile.xml

# Then copy contents of publish-profile.xml
```

---

## Environment Summary

Once configured, your environment should look like this:

### Environment: `azure-production`

**Variables (2):**
- ✅ `AZURE_FUNCTIONAPP_NAME`
- ✅ `AZURE_FUNCTION_URL`

**Secrets (1):**
- ✅ `AZURE_FUNCTIONAPP_PUBLISH_PROFILE`

**Protection Rules (Optional):**
- Deployment branches: Only `main` branch
- Required reviewers: (optional)

---

## Verification

### Test the workflow:

1. Go to **Actions** tab
2. Select **Deploy Azure Function** workflow
3. Click **Run workflow** → Select `main` branch → **Run workflow**
4. Wait ~2-3 minutes
5. Check deployment succeeds

### Test the function:

```bash
# Replace with your actual URL from AZURE_FUNCTION_URL variable
curl https://drpodcast-feed-generator.azurewebsites.net/api/HealthCheck
```

**Expected response:**
```json
{"status":"healthy","service":"DrPodcast Feed Generator"}
```

---

## Troubleshooting

### "Variable not found" error:
- Make sure variables are in the `azure-production` environment, not repository secrets
- Variable names must match exactly (case-sensitive)

### "Invalid publish profile":
- Verify you copied the **entire XML** content
- No extra spaces or newlines
- Download fresh profile from Azure Portal

### "Function App not found":
- Verify `AZURE_FUNCTIONAPP_NAME` matches your Azure Function App name exactly
- Check Azure Function App exists and is running

---

## Quick Links

- [Full Setup Guide](AZURE_SETUP.md) - Complete step-by-step instructions
- [Azure Portal](https://portal.azure.com) - Manage Azure resources
- [GitHub Environments Docs](https://docs.github.com/en/actions/deployment/targeting-different-environments/using-environments-for-deployment)
