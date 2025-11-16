# Versioning Strategy Comparison

## Current Approach Issues

### Problems
1. **Confusing PR versions**: Each PR calculates `2.0.19-pr173-a1b2c3d` even though it won't be released as 2.0.19
2. **Version changes**: Same PR gets different versions on each commit
3. **Cleanup complexity**: Need to delete prereleases after stable release
4. **Manual bumping**: Requires `[major]`, `[minor]` tags in PR descriptions

### Example
```
PR #173 commit 1: 2.0.19-pr173-a1b2c3d
PR #173 commit 2: 2.0.19-pr173-b2c3d4e  ← Same PR, different version
After merge: 2.0.19 (stable) ← Then delete all pr173 versions
```

---

## Recommended: Simplified Approach

### Strategy
- **Tags are the source of truth** - only bump version when you create a tag
- **PR builds use build metadata** - `2.0.18+pr173.a1b2c3d` (not prerelease)
- **No cleanup needed** - build metadata doesn't create releases
- **Predictable versions** - same code = same version

### Version Format (SemVer 2.0)
```
<major>.<minor>.<patch>+<build-metadata>

Examples:
- 2.0.18           ← Tagged release
- 2.0.18+main.5.a1b2c3d    ← Main branch, 5 commits since tag
- 2.0.18+pr173.a1b2c3d     ← PR #173
```

### Benefits
✅ **Simple**: No complex version calculation logic
✅ **Predictable**: Same PR always has same base version
✅ **Clear**: Version comes from tags, not guessing
✅ **No cleanup**: Build metadata doesn't create releases
✅ **NuGet compatible**: Build metadata is valid in NuGet

### Workflow
```powershell
# Get latest tag
$latestTag = git describe --tags --abbrev=0
$version = $latestTag.TrimStart('v')  # e.g., "2.0.18"

# For PRs: add build metadata
if (PR build) {
  $version = "$version+pr$prNumber.$shortSha"  # e.g., "2.0.18+pr173.a1b2c3d"
}

# For releases: check if HEAD is tagged
if (git describe --exact-match --tags HEAD) {
  # This IS a release!
  $isRelease = "true"
}
```

### When to Bump Version
**Only when you're ready to release!**

```bash
# When ready for new release:
git tag v2.0.19
git push origin v2.0.19

# GitHub Actions will:
# 1. Detect the tag on main
# 2. Build version 2.0.19
# 3. Create a GitHub release
# 4. No cleanup needed!
```

---

## Alternative: GitVersion

### If you need more automation

**Install GitVersion**:
```yaml
- name: Install GitVersion
  uses: gittools/actions/gitversion/setup@v1.1.1
  with:
    versionSpec: '5.x'

- name: Determine Version
  uses: gittools/actions/gitversion/execute@v1.1.1
  with:
    useConfigFile: true
```

**GitVersion.yml**:
```yaml
mode: ContinuousDeployment
branches:
  main:
    tag: ''
    increment: Patch
  pull-request:
    tag: pr
    increment: Inherit
```

**Benefits**:
- Automatic version bumping based on branch patterns
- Supports GitFlow, GitHub Flow, etc.
- Industry standard

**Drawbacks**:
- More complex setup
- Another dependency to maintain
- Less control over version numbers

---

## Comparison Table

| Feature | Current | Simplified (Recommended) | GitVersion |
|---------|---------|--------------------------|------------|
| **Complexity** | Medium | Low | High |
| **Dependencies** | None | None | GitVersion tool |
| **PR Versions** | `2.0.19-pr173-sha` | `2.0.18+pr173.sha` | `2.0.18-pr173.1` |
| **Version Bumping** | Manual (PR description) | Manual (git tag) | Automatic |
| **Cleanup Needed** | Yes (delete prereleases) | No | No |
| **Predictable** | No (changes per commit) | Yes | Yes |
| **NuGet Valid** | Yes (after fix) | Yes | Yes |
| **Learning Curve** | Low | Very Low | Medium |
| **Control** | Medium | High | Medium |

---

## My Recommendation

**Use the Simplified Approach** because:

1. ✅ **Your project is simple** - you don't need GitFlow or complex branching
2. ✅ **You want control** - decide when to bump versions
3. ✅ **You want predictability** - same code = same version
4. ✅ **You want simplicity** - no dependencies, no cleanup

### Migration Path

1. **Short-term**: Merge PR #176 to fix immediate build issues
2. **Long-term**: Switch to simplified approach (optional but recommended)

### To Implement Simplified Approach

**Option A: Update existing workflow**
- Modify `build-and-release.yml` version calculation
- Remove prerelease cleanup logic
- Use build metadata instead of prerelease tags

**Option B: Start fresh**
- Use the `version-simple.yml` I created
- Test it in a new PR
- Replace old workflow once validated

---

## Example Scenarios

### Scenario 1: Working on a feature
```bash
# Current tag: v2.0.18
# PR #173 builds: 2.0.18+pr173.a1b2c3d

git commit -m "Add feature"
git push
# Still builds: 2.0.18+pr173.a1b2c3d (version stays same!)

git commit -m "Fix tests"
git push
# Still builds: 2.0.18+pr173.a1b2c3d (predictable!)
```

### Scenario 2: Ready to release
```bash
# Merge PR #173 to main
# Main builds: 2.0.18+main.5.a1b2c3d (not a release yet)

# When ready for release:
git tag v2.0.19
git push origin v2.0.19
# GitHub Actions detects tag and creates release 2.0.19
```

### Scenario 3: Hotfix
```bash
# Emergency fix needed!
git commit -m "Critical security fix"
git push
# Main builds: 2.0.18+main.6.b2c3d4e

# Tag immediately:
git tag v2.0.19
git push origin v2.0.19
# Release 2.0.19 created!
```

---

## Implementation

Want me to implement the simplified approach? I can:

1. Update the existing `build-and-release.yml` workflow
2. Remove the cleanup logic
3. Switch to build metadata for PRs
4. Test it in a new PR

Just let me know! 🚀
