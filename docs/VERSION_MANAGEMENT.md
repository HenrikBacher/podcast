# Version Management System

This project uses an automated version management system that works with GitHub Actions and follows semantic versioning.

## How It Works

### Pull Request Builds
When a pull request is created:
- The build uses the current version with a `-pr-{short-sha}` suffix
- Example: `1.0.0-pr-abc1234`
- No version bumping occurs during PR builds

### Main Branch Builds
When code is pushed to the main branch (typically after merging a PR):
- The system examines the most recently merged PR for version bump instructions
- Based on the PR template checkboxes, it performs the appropriate version bump
- Updates the version in `OmmerCSharp/Ommer/Ommer.csproj`
- Creates a git tag and GitHub release

## PR Template Usage

When creating a pull request, use the checkboxes in the PR template to indicate the type of version bump:

```markdown
## Version Bump
<!-- Choose one by changing [ ] to [x] -->
- [ ] major (breaking changes)
- [ ] minor (new features)
- [ ] patch (bug fixes)
```

### Version Bump Types

- **major**: For breaking changes (1.0.0 → 2.0.0)
- **minor**: For new features (1.0.0 → 1.1.0)
- **patch**: For bug fixes (1.0.0 → 1.0.1)

If no checkbox is selected, the system defaults to a **patch** bump.

## Local Development Scripts

### Get Current Version
```powershell
.\scripts\get-version.ps1
```

Get version with commit suffix (for development builds):
```powershell
.\scripts\get-version.ps1 -WithCommitSuffix
```

### Manual Version Bump
```powershell
# Bump patch version (1.0.0 → 1.0.1)
.\scripts\bump-version.ps1 -BumpType patch

# Bump minor version (1.0.0 → 1.1.0)
.\scripts\bump-version.ps1 -BumpType minor

# Bump major version (1.0.0 → 2.0.0)
.\scripts\bump-version.ps1 -BumpType major
```

## GitHub Actions Workflow

The `build-app.yml` workflow includes these steps:

1. **Get version and bump info**: Determines the appropriate version based on context
2. **Update version in csproj**: Updates the project file with the new version
3. **Build and publish app**: Builds the application
4. **Commit version bump**: Commits the version change back to the repository
5. **Create Release**: Creates a GitHub release with the built artifacts

## Files Involved

- `OmmerCSharp/Ommer/Ommer.csproj`: Contains the current version
- `.github/pull_request_template.md`: Contains version bump checkboxes
- `.github/workflows/build-app.yml`: GitHub Actions workflow
- `scripts/bump-version.ps1`: Local version bumping script
- `scripts/get-version.ps1`: Script to get current version

## Best Practices

1. Always fill out the version bump section in PR templates
2. Use semantic versioning principles:
   - **major**: Breaking changes that require user action
   - **minor**: New features that are backward compatible
   - **patch**: Bug fixes and small improvements
3. The automated system handles version updates; avoid manual edits to the csproj version
4. Use the local scripts for development and testing purposes
