# Dependency bootstrap inventory

The machine-readable hand-written inventory is [`scripts/release-dependencies.json`](../../scripts/release-dependencies.json). `scripts/validate-release-contract.ps1` compares its exact job identifiers with the workflow and fails when a job, dependency description, bootstrap route, or safe-output list disappears.

## Job inventory

| Job | Runner | First real work | Cache-miss proof |
| --- | --- | --- | --- |
| `windows_release` | `windows-2025` | `build.bat /s` | A fresh GitHub-hosted job executes both root scripts; failure to obtain a declared dependency stops before release publication. |

## Toolchain sources

- Microsoft Build Tools: compatible Visual Studio 2022 discovery, pinned `Microsoft.VisualStudio.2022.BuildTools` 17.14.37 through winget, or the exact official Microsoft bootstrapper downloaded to a user-owned toolchain path and verified against committed SHA-256 `e0b8ea16494b4a79c68da26773131562aefecc8d87f1923c24d579c7a72e0575`.
- .NET Framework 4.7.2 reference assemblies: pinned `Microsoft.NETFramework.ReferenceAssemblies.net472` 1.0.3 from NuGet.org with SHA-512 verified against registration metadata.
- Node.js: exact version from `.node-version`, using an existing exact runtime or the official `nodejs.org` archive verified against `SHASUMS256.txt` and installed in a per-user cache.
- Exchange dependencies: exact `package-lock.json` through `npm ci`.
- Inno Setup: a compatible discovered Inno Setup 6 compiler, pinned `JRSoftware.InnoSetup` 6.7.3 through winget, or the official `jrsoftware/issrc` 6.7.3 installer downloaded to a user-owned toolchain path and verified against committed SHA-256 `9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732`.
- Squirrel.Windows: `electron-builder-squirrel-windows` 26.15.3 from the Exchange lockfile.
- Git, GitHub CLI, and PowerShell: declared capabilities of the pinned GitHub-hosted runner image, checked before build or publication work.

No bootstrap path installs a signing certificate, changes the user's persistent PowerShell execution policy, commits dependency directories, or mutates an unrelated global toolchain. Node, reference assemblies, and the Inno fallback use user-owned caches. Build Tools and Inno Setup stop with exact attempted-source evidence when every canonical installer route is unavailable.

## Local validation

```powershell
pwsh -NoProfile -File .\scripts\validate-release-contract.ps1 -SelfTest
```

The self-test first validates the real workflow and inventory, then changes ten asserted boundaries in memory one at a time. Every mutation must turn red; restoring the real bytes must remain green.
