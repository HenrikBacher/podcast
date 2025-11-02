# Azure Function Setup Guide

This guide walks you through setting up the GitHub environment and Azure credentials for automated deployment.

## Prerequisites

- Azure subscription (free tier works)
- GitHub repository admin access
- Azure CLI installed (recommended) or Azure Portal access

---

## Important: Auto-Provisioning

**The deployment workflow automatically creates Azure resources if they don't exist**, including:
- Resource group (`drpodcast-rg`)
- Storage account (`drpodcaststorage`)
- Function App (your chosen name)
- Application settings (API_KEY, BASE_URL)

You only need to:
1. Create an Azure service principal for authentication
2. Configure GitHub environment with credentials

---

## Step 1: Create Azure Service Principal

The service principal allows GitHub Actions to authenticate with Azure and manage resources.

### Using Azure CLI (Recommended)

```bash
# Login to Azure
az login

# Get your subscription ID
az account show --query id --output tsv

# Create service principal with Contributor role
# Replace YOUR_SUBSCRIPTION_ID with the ID from above
az ad sp create-for-rbac \
  --name "github-drpodcast-deployer" \
  --role Contributor \
  --scopes /subscriptions/YOUR_SUBSCRIPTION_ID \
  --sdk-auth

# This outputs JSON like:
# {
#   "clientId": "...",
#   "clientSecret": "...",
#   "subscriptionId": "...",
#   "tenantId": "...",
#   ...
# }
# Copy this ENTIRE JSON output - you'll need it for GitHub secrets
```

**Important**:
- Save the JSON output securely - it contains sensitive credentials
- The `clientSecret` is only shown once and cannot be retrieved later
- If you lose it, you'll need to create a new service principal

### Using Azure Portal (Alternative)

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Azure Active Directory** → **App registrations**
3. Click **New registration**
   - Name: `github-drpodcast-deployer`
   - Supported account types: Single tenant
   - Click **Register**
4. Note the **Application (client) ID** and **Directory (tenant) ID**
5. Navigate to **Certificates & secrets**
   - Click **New client secret**
   - Description: `GitHub Actions`
   - Expiration: Choose appropriate duration
   - Click **Add**
   - **Copy the secret value immediately** (shown only once)
6. Navigate to **Subscriptions** (search in top bar)
   - Select your subscription
   - Note the **Subscription ID**
   - Navigate to **Access control (IAM)**
   - Click **Add** → **Add role assignment**
   - Role: **Contributor**
   - Assign access to: **User, group, or service principal**
   - Select members: Search for `github-drpodcast-deployer`
   - Click **Review + assign**
7. Create JSON manually:
```json
{
  "clientId": "<Application (client) ID>",
  "clientSecret": "<Client secret value>",
  "subscriptionId": "<Subscription ID>",
  "tenantId": "<Directory (tenant) ID>"
}
```

---

## Step 2: Create GitHub Environment

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

## Step 3: Add Environment Variables

In the `azure-production` environment:

### Variables (Public)

Click **Add variable** for each:

| Name | Value | Example |
|------|-------|---------|
| `AZURE_FUNCTIONAPP_NAME` | Your chosen function app name | `drpodcast-feed-generator` |
| `BASE_URL` | Base URL for generated feeds | `https://henrikbacher.github.io/podcast` |

**Notes:**
- `AZURE_FUNCTIONAPP_NAME`: Choose a globally unique name (will be created automatically if it doesn't exist)
- `BASE_URL`: The base URL where your podcast feeds will be hosted

---

## Step 4: Add Environment Secrets

In the `azure-production` environment:

### Secrets (Private)

Click **Add secret** for each:

| Name | Value |
|------|-------|
| `AZURE_CREDENTIALS` | Paste the entire JSON from Step 1 |
| `DR_API_KEY` | Your DR API key for accessing podcast data |

**Important:** These secrets are sensitive - treat them like passwords!

---

## Step 5: Verify Setup

### Checklist:

- [ ] Azure service principal created
- [ ] GitHub environment `azure-production` created
- [ ] Environment variable `AZURE_FUNCTIONAPP_NAME` set
- [ ] Environment variable `BASE_URL` set
- [ ] Environment secret `AZURE_CREDENTIALS` set (service principal JSON)
- [ ] Environment secret `DR_API_KEY` set

### Test Deployment:

1. Push to `main` branch or manually trigger workflow in **Actions** tab
2. Go to **Actions** tab in GitHub
3. Watch **Deploy Azure Function** workflow
4. The workflow will:
   - Authenticate with Azure using service principal
   - Create resource group (if needed)
   - Create storage account (if needed)
   - Create function app (if needed)
   - Configure application settings
   - Deploy the function code
5. First deployment takes ~3-5 minutes (subsequent deployments ~2 minutes)

### Verify Function is Running:

After deployment completes, test the health endpoint:

```bash
# Replace [your-app-name] with your AZURE_FUNCTIONAPP_NAME
curl https://[your-app-name].azurewebsites.net/api/HealthCheck

# Expected response:
# {"status":"healthy","service":"DrPodcast Feed Generator"}
```

---

## Step 6: Test Feed Generation

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
- Check `AZURE_CREDENTIALS` secret is valid JSON from service principal creation
- Verify service principal has Contributor role on subscription
- Check workflow logs in GitHub Actions for specific Azure CLI errors

**Function not running:**
- Verify `DR_API_KEY` is valid
- Check Azure Portal → Function App → Application Insights for errors
- Ensure auto-provisioned resources were created successfully

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

1. **Rotate service principal credentials regularly:**
   - Create new client secret every 90 days
   - Update GitHub `AZURE_CREDENTIALS` secret
   - Delete old client secret from Azure AD

2. **Use least-privilege access:**
   - Service principal only needs Contributor role on specific resource group
   - Consider scoping to resource group instead of full subscription:
     ```bash
     az ad sp create-for-rbac \
       --name "github-drpodcast-deployer" \
       --role Contributor \
       --scopes /subscriptions/YOUR_SUB_ID/resourceGroups/drpodcast-rg \
       --sdk-auth
     ```

3. **Enable authentication:**
   - Function App → **Authentication** → **Add identity provider**
   - Protect HTTP trigger endpoints

4. **Monitor access:**
   - Check Application Insights regularly
   - Set up alerts for failures
   - Review service principal sign-in logs in Azure AD

---

## Quick Reference

### URLs:
- **Function App**: `https://[app-name].azurewebsites.net`
- **Health Check**: `https://[app-name].azurewebsites.net/api/HealthCheck`
- **Manual Trigger**: `https://[app-name].azurewebsites.net/api/GenerateFeedsHttp`

### GitHub Environment:
- **Name**: `azure-production`
- **Variables**: `AZURE_FUNCTIONAPP_NAME`, `BASE_URL`
- **Secrets**: `AZURE_CREDENTIALS`, `DR_API_KEY`

### Azure Resources (Auto-Provisioned):
- **Resource Group**: `drpodcast-rg` (West Europe)
- **Storage Account**: `drpodcaststorage`
- **Function App**: Your chosen name from `AZURE_FUNCTIONAPP_NAME`
- **Plan**: Consumption (Serverless)
- **Runtime**: .NET 8 Isolated

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
- **.NET Isolated Functions**: https://aka.ms/dotnet-isolated-process

For issues with this setup, open a GitHub issue in the repository.
