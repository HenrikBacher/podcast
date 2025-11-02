# GitHub Environment Setup - Quick Start

This is a quick reference for setting up the `azure-production` GitHub environment with auto-provisioning.

## Important: Auto-Provisioning

**The deployment workflow automatically creates Azure resources if they don't exist**. You only need to:
1. Create an Azure service principal
2. Configure GitHub environment with credentials

## Prerequisites

1. **Create Azure Service Principal:**

```bash
# Login to Azure
az login

# Get your subscription ID
SUBSCRIPTION_ID=$(az account show --query id --output tsv)

# Create service principal with Contributor role
az ad sp create-for-rbac \
  --name "github-drpodcast-deployer" \
  --role Contributor \
  --scopes /subscriptions/$SUBSCRIPTION_ID \
  --sdk-auth

# Copy the ENTIRE JSON output - you'll need it for GitHub secrets
```

---

## Create Environment

1. **Go to:** Repository → Settings → Environments
2. **Click:** New environment
3. **Name:** `azure-production`
4. **Click:** Configure environment

---

## Add Environment Variables

Click **Add variable** for each:

### Required Variables

| Variable Name | Example Value | Description |
|---------------|---------------|-------------|
| `AZURE_FUNCTIONAPP_NAME` | `drpodcast-feed-generator` | Your chosen function app name (will be created automatically) |
| `BASE_URL` | `https://henrikbacher.github.io/podcast` | Base URL where feeds will be hosted |

**Notes:**
- Function app name must be globally unique in Azure
- The deployment workflow will create the function app if it doesn't exist

---

## Add Environment Secrets

Click **Add secret** for each:

### Required Secrets

| Secret Name | Where to Get It |
|-------------|-----------------|
| `AZURE_CREDENTIALS` | Service principal JSON from Prerequisites step above |
| `DR_API_KEY` | Your DR API key for accessing podcast data |

**Steps to add AZURE_CREDENTIALS:**

1. Copy the entire JSON output from the `az ad sp create-for-rbac` command
2. Go to GitHub → Settings → Environments → azure-production
3. Click **Add secret**
4. Name: `AZURE_CREDENTIALS`
5. Value: Paste the entire JSON
6. Click **Add secret**

**Example format** (do not use these values):
```json
{
  "clientId": "12345678-1234-1234-1234-123456789012",
  "clientSecret": "your-secret-value",
  "subscriptionId": "12345678-1234-1234-1234-123456789012",
  "tenantId": "12345678-1234-1234-1234-123456789012"
}
```

---

## Environment Summary

Once configured, your environment should look like this:

### Environment: `azure-production`

**Variables (2):**
- ✅ `AZURE_FUNCTIONAPP_NAME`
- ✅ `BASE_URL`

**Secrets (2):**
- ✅ `AZURE_CREDENTIALS`
- ✅ `DR_API_KEY`

**Protection Rules (Optional):**
- Deployment branches: Only `main` branch
- Required reviewers: (optional)

---

## Verification

### Test the workflow:

1. Go to **Actions** tab
2. Select **Deploy Azure Function** workflow
3. Click **Run workflow** → Select `main` branch → **Run workflow**
4. Wait ~3-5 minutes (first deployment creates Azure resources)
5. Workflow will:
   - Authenticate with Azure
   - Create resource group (if needed)
   - Create storage account (if needed)
   - Create function app (if needed)
   - Configure app settings
   - Deploy function code
6. Check deployment succeeds

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

### "AZURE_CREDENTIALS invalid" error:
- Verify you copied the **entire JSON** output from `az ad sp create-for-rbac`
- Ensure JSON is valid (no extra spaces, complete braces)
- Check service principal has Contributor role on subscription

### "Authorization failed" error:
- Verify service principal has correct permissions
- Check the subscription ID in AZURE_CREDENTIALS matches your Azure subscription
- Ensure service principal hasn't expired (check Azure AD → App registrations)

---

## Quick Links

- [Full Setup Guide](AZURE_SETUP.md) - Complete step-by-step instructions
- [Azure Portal](https://portal.azure.com) - Manage Azure resources
- [GitHub Environments Docs](https://docs.github.com/en/actions/deployment/targeting-different-environments/using-environments-for-deployment)
