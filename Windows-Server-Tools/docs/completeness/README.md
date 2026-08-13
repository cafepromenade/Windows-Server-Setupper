# Completeness

This category records release-blocking feature inventories for user-facing surfaces. The inventories are evidence maps, not feature wish lists: an item remains blocked until implementation, documentation, localization, persistence where applicable, focused tests, built-artifact interaction, and a real capture are all linked.

## Inventories

- [Primary WPF universal-feature inventory](./wpf-universal-feature-inventory.md)
- [Application branding](../branding/README.md)

## Verification

Run the structural inventory check from the repository root:

```powershell
pwsh -NoProfile -File .\Windows-Server-Tools\Windows-Server-Tools.Tests\verify-wpf-universal-inventory.ps1
```

Run the release-enforcement form to require every applicable row to be ready:

```powershell
pwsh -NoProfile -File .\Windows-Server-Tools\Windows-Server-Tools.Tests\verify-wpf-universal-inventory.ps1 -Release
```

The release form intentionally exits nonzero while any row is `PARTIAL` or `MISSING`. `DOCUMENTED-NOT-APPLICABLE` is accepted only for a row whose rationale names the exact absent product capability.
