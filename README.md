# Windows Server Setupper

Windows Server Setupper contains two Windows administration applications: a resilient .NET Framework 4.7.2 WPF server-setup application and the Exchange Auto Installer. Both can change server roles, network settings, scheduled tasks, directory-service data, and Exchange configuration; use them only on an appropriate test server and review each requested operation.

## Index

- [Current release state](#current-release-state)
- [Applications](#applications)
- [Delivery and documentation](#delivery-and-documentation)
- [Roadmap](./ROADMAP.md) and [handoff](./HANDOFF.md)

## Current release state

The repository contains source and packaging routes for a combined Windows release, but the final combined release is **pending**. This README does not assert a current final build, package, GitHub Actions run, GitHub Release, installer download, GitHub Pages deployment, installation result, or UI capture.

The previously published recovery-only WPF artifact is historical evidence only. It is not evidence that the current combined WPF and Exchange release has been built or published.

## Applications

### Resilient WPF server tools

The WPF application provides server configuration and recovery-oriented flows. Its source records durable state, exposes recovery information, and treats uncertain outcomes as requiring an explicit reconciliation rather than guessing success or failure. See the [reliability documentation](./Windows-Server-Tools/docs/reliability/README.md).

### Exchange Auto Installer

The Exchange Auto Installer guides a local administrator through a mostly pre-filled, staged Microsoft Exchange Server installation. It does not pre-fill credentials. Its staged plan includes explicit stop, retry, and resume handling rather than a fire-and-forget installation path.

- **Exchange media:** an administrator can select local media or use the repository Cheap LFS route. The route verifies immutable part metadata before hydration and validates the reconstructed ISO before installation use. The ISO is not copied into Git history, attached to a product release, or transferred with standard Git LFS.
- **Managed OpenCode repair:** the optional repair adviser uses a pinned managed OpenCode installation and a bounded, redacted diagnostic workspace. Its YOLO mode is explicitly opt-in and off by default; it is limited to the application's fixed repair-action catalog and cannot approve arbitrary commands, credentials, unrelated directories, policy bypasses, or hidden network destinations.

See the [Exchange Auto Installer guide](./Windows-Server-Tools/Exchange-Auto-Installer/README.md) for its local safety and packaging boundaries.

## Delivery and documentation

- The WPF route produces an **unsigned Inno Setup** installer.
- The Exchange route produces an **unsigned Squirrel.Windows** setup executable, `RELEASES` index, full package, and any packager-generated delta packages.
- Code signing is intentionally disabled for both artifact families. Windows may show unknown-publisher or SmartScreen warnings; hashes and release metadata provide integrity information, not publisher authenticity.
- The [Windows release contract](./docs/release/windows-release.md) describes the expected build, package, and publication evidence without claiming that it exists for the pending final release.
- The documentation-site route is `docs-site/build.bat /ci`. It creates the Cloudflare Worker-compatible `docs-site/dist` output and the GitHub Pages `docs-site/pages-dist` export without running its focused tests. A GitHub Pages URL is not asserted until that exact export is published and verified.

<details>
<summary><strong>Local build and installer routes</strong></summary>

`build.bat /s` is the supported local runnable-application route. `build-installer.bat /s` is the supported local installer route. They build from the checkout but do not publish, tag, or create a release. Their outputs are generated artifacts and remain outside Git history.

No command in this documentation update was run to build, package, verify, install, or publish either application.

</details>

<details>
<summary><strong>Release evidence still required</strong></summary>

Before a final combined release can be described as shipped, its exact source revision must be built and packaged; the resulting unsigned WPF and Exchange artifacts must be recorded; the release workflow and non-draft release must be read back; and the actual asset set, target revision, hashes, downloadability, and any published documentation site must be verified. This README deliberately leaves those values absent until that evidence exists.

</details>

<details>
<summary><strong>Documentation map</strong></summary>

- [Release documentation](./docs/release/README.md)
- [Windows release contract](./docs/release/windows-release.md)
- [Dependency bootstrap inventory](./docs/release/dependency-bootstrap.md)
- [WPF reliability documentation](./Windows-Server-Tools/docs/reliability/README.md)
- [Exchange Auto Installer documentation](./Windows-Server-Tools/Exchange-Auto-Installer/README.md)

</details>
