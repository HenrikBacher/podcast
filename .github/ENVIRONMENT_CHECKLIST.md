# GitHub Environment Checklist

Use this checklist when setting up the `azure-production` environment with auto-provisioning.

## Important: Auto-Provisioning

**The deployment workflow automatically creates Azure resources if they don't exist**. This checklist focuses on setting up GitHub environment and Azure authentication.

## Pre-requisites

- [ ] Azure subscription (free tier works)
- [ ] Azure CLI installed (or access to Azure Portal)
- [ ] GitHub repository admin access

---

## Step 1: Create Azure Service Principal

- [ ] Login to Azure CLI: `az login`
- [ ] Get subscription ID: `az account show --query id --output tsv`
- [ ] Create service principal:
  ```bash
  az ad sp create-for-rbac \
    --name "github-drpodcast-deployer" \
    --role Contributor \
    --scopes /subscriptions/YOUR_SUBSCRIPTION_ID \
    --sdk-auth
  ```
- [ ] Copy **entire JSON output** (save it securely - you'll need it in Step 4)

---

## Step 2: Create GitHub Environment

- [ ] Go to: **Settings** → **Environments**
- [ ] Click: **New environment**
- [ ] Enter name: `azure-production`
- [ ] Click: **Configure environment**

---

## Step 3: Add Environment Variables

Copy the following values and add as variables:

#### Variable 1: AZURE_FUNCTIONAPP_NAME
- [ ] Click **Add variable**
- [ ] Name: `AZURE_FUNCTIONAPP_NAME`
- [ ] Value: `____________________________`
  ```
  Example: drpodcast-feed-generator

  Notes:
  - Choose a globally unique name
  - This function app will be created automatically by the workflow
  - Only lowercase letters, numbers, and hyphens allowed
  ```

#### Variable 2: BASE_URL
- [ ] Click **Add variable**
- [ ] Name: `BASE_URL`
- [ ] Value: `____________________________`
  ```
  Example: https://henrikbacher.github.io/podcast

  Notes:
  - Base URL where your podcast feeds will be hosted
  - Include https:// prefix
  ```

---

## Step 4: Add Environment Secrets

#### Secret 1: AZURE_CREDENTIALS
- [ ] Click **Add secret**
- [ ] Name: `AZURE_CREDENTIALS`
- [ ] Value: *(paste entire JSON from Step 1)*
  ```json
  {
    "clientId": "...",
    "clientSecret": "...",
    "subscriptionId": "...",
    "tenantId": "..."
  }
  ```
- [ ] Click **Add secret**

#### Secret 2: DR_API_KEY
- [ ] Click **Add secret**
- [ ] Name: `DR_API_KEY`
- [ ] Value: `____________________________`
  ```
  Your DR API key for accessing podcast data
  ```
- [ ] Click **Add secret**

---

## Step 5: Verification

### Environment Summary

Your `azure-production` environment should show:

```
📊 Environment: azure-production

Variables (2):
  ✓ AZURE_FUNCTIONAPP_NAME
  ✓ BASE_URL

Secrets (2):
  ✓ AZURE_CREDENTIALS
  ✓ DR_API_KEY

Deployment branches:
  ✓ main (optional protection)
```

### Test Deployment

- [ ] Go to **Actions** tab
- [ ] Click **Deploy Azure Function** workflow
- [ ] Click **Run workflow** → Select `main` → **Run workflow**
- [ ] Wait 3-5 minutes (first deployment creates Azure resources)
- [ ] Verify workflow completes successfully:
  - [ ] ✅ Azure Login
  - [ ] ✅ Create resource group (if needed)
  - [ ] ✅ Create storage account (if needed)
  - [ ] ✅ Create function app (if needed)
  - [ ] ✅ Configure app settings
  - [ ] ✅ Deploy function code

### Test Function

- [ ] Open terminal and run:
  ```bash
  curl https://[your-app-name].azurewebsites.net/api/HealthCheck
  ```
- [ ] Verify response:
  ```json
  {"status":"healthy","service":"DrPodcast Feed Generator"}
  ```

### Verify Azure Resources (Optional)

- [ ] Login to Azure Portal
- [ ] Navigate to Resource Groups
- [ ] Verify `drpodcast-rg` exists
- [ ] Inside resource group, verify:
  - [ ] Storage account: `drpodcaststorage`
  - [ ] Function App: Your chosen name
  - [ ] Application Insights (auto-created)

---

## Common Issues

### ❌ Variable not found
**Fix:** Ensure variables are added to the **environment**, not repository secrets

### ❌ Invalid AZURE_CREDENTIALS
**Fix:** Verify you copied the complete JSON output from `az ad sp create-for-rbac` (no truncation)

### ❌ Authorization failed
**Fix:**
- Check service principal has Contributor role on subscription
- Verify subscription ID in AZURE_CREDENTIALS matches your subscription
- Ensure service principal hasn't been deleted in Azure AD

### ❌ Resource creation failed
**Fix:**
- Check Azure subscription has available quota
- Verify resource names don't conflict with existing resources
- Ensure service principal has permissions to create resources

---

## Reference Values Template

Use this template to track your values (don't commit with real values!):

```
Service Principal Name: github-drpodcast-deployer
Azure Subscription ID: ________________________
Function App Name: ________________________
Base URL: ________________________
DR API Key: ************************
GitHub Environment: azure-production
Deployment Date: ________________________
```

---

## Next Steps

Once environment is configured:

1. ✅ Merge PR to main branch
2. ✅ Automatic deployment will trigger
3. ✅ Monitor first deployment in Actions tab
4. ✅ Verify feeds generate correctly
5. ✅ Set up monitoring alerts (optional)

---

**Setup Time:** ~10-15 minutes
**Difficulty:** Beginner-friendly
**Support:** See [AZURE_SETUP.md](AZURE_SETUP.md) for detailed instructions
