# Windows Server Setupper

Windows Server Setupper contains Windows desktop tools for configuring server roles and a guided Exchange installation. The repository currently ships two Windows applications:

- the .NET Framework 4.7.2 WPF application under `Windows-Server-Tools/Windows-Server-Tools`;
- the Electron-based Exchange Auto Installer under `Windows-Server-Tools/Exchange-Auto-Installer`.

> [!WARNING]
> These tools change operating-system roles, network settings, security settings, scheduled tasks, and directory-service data. Evaluate them on an appropriate test server, review every requested operation, and use administrative rights only where the operation requires them.

## Install and release status

The repository contains a complete Windows-only build-and-release contract for both applications. A successful `Windows release` GitHub Actions run produces one unique non-draft release with:

- the unsigned WPF installer;
- the unsigned Exchange Auto Installer `Setup.exe`;
- the Squirrel.Windows `RELEASES` index, full package, and any generated delta packages;
- SHA-256, source-commit, line-count, dependency-inventory, runner-context, and artifact-manifest evidence.

No current combined-release link is asserted here until that workflow has completed and its assets have been read back successfully. The existing recovery-only release remains a historical WPF artifact, not proof that the combined contract has run.

GitHub Actions intentionally performs no tests, lint, type checking, static analysis, accessibility checks, or screenshots. That accepted trade allows a release to publish from a commit whose local checks would fail; the first report may come from someone running an installer. Local checks remain the responsibility of the change that is pushed.

All installers are intentionally unsigned and may trigger Windows unknown-publisher or SmartScreen warnings. The project never requests, discovers, generates, or uses a code-signing certificate.

## One-click local build

From a Windows command prompt or PowerShell session:

```powershell
.\build.bat /s
.\build-installer.bat /s
```

`build.bat` bootstraps the required Microsoft build tools, pinned .NET Framework 4.7.2 reference assemblies, Node.js version from `.node-version`, NuGet packages, and the exact Exchange npm lockfile. It then builds and validates both runnable applications.

`build-installer.bat` reuses or rebuilds the commit-exact WPF application, creates the unsigned Inno Setup installer, packages the Exchange application through an isolated task-owned copy with Squirrel.Windows, and validates setup executables, `RELEASES`, full/delta packages, provenance, hashes, shared package version, and unsigned state. In Actions, both installer families carry the unique `1.<run-number>.<run-attempt>` version. A local build uses the committed Exchange package version for both families. The script never publishes, creates a tag, or creates a release.

Without `/s` (or `--silent` or `SILENT=1`), `build.bat` offers to launch the primary WPF application after a successful build. Silent mode never prompts or opens a window.

Expected generated outputs are ignored and remain outside Git history:

```text
Windows-Server-Tools/Windows-Server-Tools/bin/Installer/WindowsServerTools-Setup-<commit>.exe
Windows-Server-Tools/Exchange-Auto-Installer/dist/squirrel-windows/*-Setup.exe
Windows-Server-Tools/Exchange-Auto-Installer/dist/squirrel-windows/RELEASES
Windows-Server-Tools/Exchange-Auto-Installer/dist/squirrel-windows/*-full.nupkg
Windows-Server-Tools/Exchange-Auto-Installer/dist/squirrel-windows/*-delta.nupkg (when generated)
```

## Documentation

- [Release documentation](./docs/release/README.md)
- [Windows release contract](./docs/release/windows-release.md)
- [Dependency bootstrap inventory](./docs/release/dependency-bootstrap.md)
- [Reliability documentation](./Windows-Server-Tools/docs/reliability/README.md)
- [Error recovery and resumable operations](./Windows-Server-Tools/docs/reliability/error-recovery.md)
- [Roadmap](./ROADMAP.md)
- [Current handoff](./HANDOFF.md)

<details>
<summary><strong>Reproduce the release-contract checks</strong></summary>

```powershell
pwsh -NoProfile -File .\scripts\validate-release-contract.ps1 -SelfTest
pwsh -NoProfile -File .\scripts\test-release-assets.ps1
pwsh -NoProfile -File .\scripts\count-lines.ps1
```

The first command validates the hand-written job/dependency inventory and proves ten deliberate release-contract defects turn the check red. The second proves eleven missing or corrupt Squirrel asset cases turn red. The line counter requires exact committed tracked bytes and reports source, tests, styles/markup, tooling, excluded areas, grand totals, and surviving-line attribution.

</details>

<details>
<summary><strong>Large Exchange installation media boundary</strong></summary>

The repository tracks only Cheap LFS pointer metadata for the Exchange ISO. The build and release workflow does not hydrate, copy, upload, or attach the multi-gigabyte ISO, and it never uses standard Git LFS. Runtime download/reassembly must validate every compressed part and the final ISO before the application offers the media to an installation stage.

</details>

<details>
<summary><strong>Dim-sum release code name</strong></summary>

The release workflow resolves an unused dish only from the public `Ding-Ding-Projects/dim-sum-photos` catalog and verifies that the photo is present in a published `catalog-v1*` release. Release notes link to that public photo. They do not download, vendor, copy, or attach it to this repository's release. Catalog unavailability never blocks publication; the release ships without a code name instead of reusing or inventing one.

</details>
