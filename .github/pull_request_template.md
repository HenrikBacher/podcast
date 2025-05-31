# Pull Request

## Description

Brief description of the changes in this PR.

## Type of Change

Please check the type of change your PR introduces:

- [ ] 🐛 Bug fix (patch version bump)
- [ ] ✨ New feature (minor version bump)
- [ ] 💥 Breaking change (major version bump)
- [ ] 📝 Documentation update
- [ ] 🔧 Configuration change
- [ ] 🧪 Test update

## Version Bump

This section controls the semantic version bump when this PR is merged:

- [ ] **[PATCH]** - Bug fixes, small improvements (x.x.X)
- [ ] **[MINOR]** - New features, backwards compatible (x.X.x)
- [ ] **[MAJOR]** - Breaking changes, API changes (X.x.x)

> **Note:** If none of the above are checked, a **patch** version bump will be applied by default.

## Checklist

- [ ] My code follows the project's coding standards
- [ ] I have performed a self-review of my own code
- [ ] I have commented my code, particularly in hard-to-understand areas
- [ ] I have made corresponding changes to the documentation (if applicable)
- [ ] My changes generate no new warnings
- [ ] I have added tests that prove my fix is effective or that my feature works (if applicable)
- [ ] New and existing unit tests pass locally with my changes

## Testing

Describe the tests that you ran to verify your changes. Provide instructions so reviewers can reproduce.

## Screenshots (if applicable)

Add screenshots to help explain your changes.

## Additional Notes

Any additional information that would be helpful for reviewers.

---

### Version Bump Keywords

The CI/CD pipeline will automatically detect version bumps based on:

- **Major:** Keywords like "breaking change", "major version", or "[major]" in this PR description
- **Minor:** Keywords like "feature", "minor version", or "[minor]" in this PR description
- **Patch:** Default for all other changes or keywords like "bug fix", "patch", or "[patch]"
