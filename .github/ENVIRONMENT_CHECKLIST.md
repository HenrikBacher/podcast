# GitHub Environment Checklist

Use this checklist when setting up the `azure-production` environment.

## Pre-requisites

- [ ] Azure Function App created in Azure Portal
- [ ] Application settings configured in Azure (API_KEY, BASE_URL)
- [ ] GitHub repository admin access

---

## GitHub Environment Configuration

### 1. Create Environment

- [ ] Go to: **Settings** → **Environments**
- [ ] Click: **New environment**
- [ ] Enter name: `azure-production`
- [ ] Click: **Configure environment**

---

### 2. Add Environment Variables

Copy the following values from Azure and add as variables:

#### Variable 1: AZURE_FUNCTIONAPP_NAME
- [ ] Click **Add variable**
- [ ] Name: `AZURE_FUNCTIONAPP_NAME`
- [ ] Value: `____________________________`
  ```
  Example: drpodcast-feed-generator

  Where to find:
  Azure Portal → Function App → Name shown at top
  ```

#### Variable 2: AZURE_FUNCTION_URL
- [ ] Click **Add variable**
- [ ] Name: `AZURE_FUNCTION_URL`
- [ ] Value: `____________________________`
  ```
  Example: https://drpodcast-feed-generator.azurewebsites.net

  Where to find:
  Azure Portal → Function App → Overview → URL field
  (Include https:// prefix)
  ```

---

### 3. Add Environment Secret

#### Secret: AZURE_FUNCTIONAPP_PUBLISH_PROFILE
- [ ] Get publish profile:
  - [ ] Azure Portal → Function App → Overview
  - [ ] Click **Get publish profile** (downloads XML file)
  - [ ] Open file in text editor
  - [ ] Copy **all** XML content (from `<?xml` to `</publishData>`)

- [ ] Add to GitHub:
  - [ ] Click **Add secret**
  - [ ] Name: `AZURE_FUNCTIONAPP_PUBLISH_PROFILE`
  - [ ] Value: *(paste entire XML content)*
  - [ ] Click **Add secret**

---

## Verification

### Environment Summary

Your `azure-production` environment should show:

```
📊 Environment: azure-production

Variables (2):
  ✓ AZURE_FUNCTIONAPP_NAME
  ✓ AZURE_FUNCTION_URL

Secrets (1):
  ✓ AZURE_FUNCTIONAPP_PUBLISH_PROFILE

Deployment branches:
  ✓ main (optional protection)
```

### Test Deployment

- [ ] Go to **Actions** tab
- [ ] Click **Deploy Azure Function** workflow
- [ ] Click **Run workflow** → Select `main` → **Run workflow**
- [ ] Wait 2-3 minutes
- [ ] Verify: ✅ Deployment successful

### Test Function

- [ ] Open terminal and run:
  ```bash
  curl https://[your-app-name].azurewebsites.net/api/HealthCheck
  ```
- [ ] Verify response:
  ```json
  {"status":"healthy","service":"DrPodcast Feed Generator"}
  ```

---

## Common Issues

### ❌ Variable not found
**Fix:** Ensure variables are added to the **environment**, not repository secrets

### ❌ Invalid publish profile
**Fix:** Verify you copied the complete XML (no truncation)

### ❌ Function App not found
**Fix:** Check `AZURE_FUNCTIONAPP_NAME` matches Azure exactly (case-sensitive)

### ❌ Deployment timeout
**Fix:** Check Azure Function App is running and healthy in Azure Portal

---

## Reference Values Template

Use this template to track your values (don't commit with real values!):

```
Azure Function App Name: ________________________
Azure Function URL: https://________________________.azurewebsites.net
Publish Profile Location: ________________________
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
