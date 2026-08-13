# Dependency bootstrap inventory

The machine-readable hand-written inventory is [`scripts/release-dependencies.json`](../../scripts/release-dependencies.json). `scripts/validate-release-contract.ps1` compares its exact job identifiers with the workflow and fails when a job, dependency description, bootstrap route, or safe-output list disappears.

## Job inventory

| Job | Runner | First real work | Cache-miss proof |
| --- | --- | --- | --- |
| `windows_release` | `windows-2025` | `build.bat /s` | A fresh GitHub-hosted job executes both root scripts; failure to obtain a declared dependency stops before release publication. |

## Toolchain sources

- Microsoft Build Tools: Visual Studio 2022 discovery, then the canonical `Microsoft.VisualStudio.2022.BuildTools` winget package.
- .NET Framework 4.7.2 reference assemblies: pinned `Microsoft.NETFramework.ReferenceAssemblies.net472` 1.0.3 from NuGet.org with SHA-512 verified against registration metadata.
- Node.js: exact version from `.node-version`, using an existing exact runtime or the official `nodejs.org` archive verified against `SHASUMS256.txt` and installed in a per-user cache.
- Exchange dependencies: exact `package-lock.json` through `npm ci`.
- Inno Setup: discovered Inno Setup 6 or the canonical `JRSoftware.InnoSetup` winget package.
- Squirrel.Windows: `electron-builder-squirrel-windows` 26.15.3 from the Exchange lockfile.
- Git, GitHub CLI, and PowerShell: declared capabilities of the pinned GitHub-hosted runner image, checked before build or publication work.

No bootstrap path installs a signing certificate, changes the user's persistent PowerShell execution policy, commits dependency directories, or mutates an unrelated global toolchain. Node and the reference assemblies use user-owned caches. Build Tools and Inno Setup stop with exact attempted-source evidence when their canonical installer route is unavailable.

## Local validation

```powershell
pwsh -NoProfile -File .\scripts\validate-release-contract.ps1 -SelfTest
```

The self-test first validates the real workflow and inventory, then changes eight asserted boundaries in memory one at a time. Every mutation must turn red; restoring the real bytes must remain green.
