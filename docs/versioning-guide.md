# Versioning Guide

This project uses **simplified tag-based versioning** for predictable and clean version management.

## 🎯 Quick Start

### For Development (PRs)
**Just create your PR** - versioning is automatic!

- Your PR will build as: `2.0.18+pr173.a1b2c3d`
- Same PR, same version (predictable!)
- Build metadata doesn't create releases

### For Releases
**Tag when ready to release:**

```bash
# Decide on version (major.minor.patch)
git tag v2.1.0
git push origin v2.1.0
```

That's it! GitHub Actions will:
1. Detect the tag
2. Build version `2.1.0`
3. Create a GitHub release
4. Upload binaries for all platforms

---

## 📖 How It Works

### Version Format (SemVer 2.0)

```
<major>.<minor>.<patch>+<build-metadata>
```

**Examples:**
- `2.0.18` - Tagged release
- `2.0.18+main.5.a1b2c3d` - Main branch, 5 commits since tag
- `2.0.18+pr173.a1b2c3d` - PR #173

### Version Sources

| Context | Version | Creates Release? |
|---------|---------|------------------|
| PR build | `2.0.18+pr173.sha` | ❌ No |
| Main (untagged) | `2.0.18+main.5.sha` | ❌ No |
| Main (tagged) | `2.1.0` | ✅ **Yes!** |

---

## 🚀 Release Workflow

### Standard Release

```bash
# 1. Ensure you're on main and up to date
git checkout main
git pull

# 2. Tag the current commit
git tag v2.1.0

# 3. Push the tag
git push origin v2.1.0

# 4. GitHub Actions handles the rest!
```

### Hotfix Release

```bash
# 1. Create hotfix, merge to main
gh pr create --title "Critical security fix"
gh pr merge --squash

# 2. Tag immediately
git pull
git tag v2.0.19
git push origin v2.0.19
```

---

## 📊 Semantic Versioning

Follow [SemVer 2.0](https://semver.org/) for version bumps:

### MAJOR version (`3.0.0`)
**Breaking changes** - incompatible API changes

Examples:
- Removing command-line arguments
- Changing output format
- Dropping support for a platform

### MINOR version (`2.1.0`)
**New features** - backward-compatible additions

Examples:
- Adding new command-line options
- Adding new feed formats
- Adding new functionality

### PATCH version (`2.0.1`)
**Bug fixes** - backward-compatible fixes

Examples:
- Fixing crashes
- Correcting output errors
- Security patches

---

## 🔍 Checking Versions

### Latest Release
```bash
gh release view --json tagName,name
```

### All Releases
```bash
gh release list
```

### Current Build Version
```bash
# In GitHub Actions logs, look for:
# 📦 Final version: 2.0.18+pr173.a1b2c3d
```

---

## ❓ FAQ

### Q: How do I know what version to tag?
**A:** Look at the latest release and follow SemVer:
- Breaking changes? Bump MAJOR
- New features? Bump MINOR
- Bug fixes? Bump PATCH

### Q: What if I tag the wrong version?
**A:** Delete the tag and re-create:
```bash
git tag -d v2.1.0              # Delete locally
git push --delete origin v2.1.0  # Delete remotely
git tag v2.0.19                # Correct tag
git push origin v2.0.19
```

### Q: Can I create a release from an old commit?
**A:** Yes! Checkout the commit and tag it:
```bash
git checkout abc123
git tag v2.0.19
git push origin v2.0.19
```

### Q: Why don't PR builds create releases?
**A:** PRs use **build metadata** (`+pr173`), not prerelease tags (`-pr173`). This means:
- ✅ Clear that it's not a release
- ✅ No cleanup needed
- ✅ Same PR = same version

### Q: What happened to automatic version bumping?
**A:** It was removed because:
- ❌ Confusing (PR shows `2.0.19` but won't release as that)
- ❌ Unpredictable (version changed on every commit)
- ❌ Required cleanup of prereleases

The new approach is:
- ✅ Simple (tags = releases)
- ✅ Predictable (same code = same version)
- ✅ Clear (you decide when to release)

---

## 🎓 Best Practices

### 1. Release Regularly
Don't accumulate many changes - release often:
- **Good**: Release v2.0.19 → v2.0.20 → v2.0.21 (small changes)
- **Bad**: Release v2.0.19 → v2.1.0 (100 changes)

### 2. Use Meaningful Tags
Always use the `v` prefix:
- ✅ `v2.1.0`
- ❌ `2.1.0`

### 3. Document Releases
Edit the GitHub release after creation to add:
- What changed
- Breaking changes (if any)
- Migration guide (if needed)

### 4. Test Before Tagging
Ensure main builds successfully before tagging:
1. Merge PR
2. Wait for main build to pass
3. Then tag

---

## 📝 Examples

### Example 1: Feature Release
```bash
# Merged PR #180: "Add XML sitemap generation"
# This is a new feature (backward-compatible)

git pull
git tag v2.1.0  # Bump MINOR version
git push origin v2.1.0
```

### Example 2: Bug Fix Release
```bash
# Merged PR #181: "Fix crash when feed has no episodes"
# This is a bug fix

git pull
git tag v2.0.19  # Bump PATCH version
git push origin v2.0.19
```

### Example 3: Multiple Fixes
```bash
# Merged PRs #182, #183, #184 (all bug fixes)
# You can either:

# Option A: Release after each
git tag v2.0.19 && git push origin v2.0.19
# ... merge next PR ...
git tag v2.0.20 && git push origin v2.0.20

# Option B: Batch release
# ... merge all PRs ...
git tag v2.0.19 && git push origin v2.0.19
```

---

## 🔗 Resources

- [Semantic Versioning 2.0](https://semver.org/)
- [Git Tagging](https://git-scm.com/book/en/v2/Git-Basics-Tagging)
- [GitHub Releases](https://docs.github.com/en/repositories/releasing-projects-on-github)
