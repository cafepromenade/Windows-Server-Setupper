# Windows Server Setupper

Windows Server Setupper contains two Windows administration applications: a resilient .NET Framework 4.7.2 WPF server-setup application and the Exchange Auto Installer. Both can change server roles, network settings, scheduled tasks, directory-service data, and Exchange configuration; use them only on an appropriate test server and review each requested operation.

## Index

- [Current release state](#current-release-state)
- [Applications](#applications)
- [Delivery and documentation](#delivery-and-documentation)
- [Roadmap](./ROADMAP.md) and [handoff](./HANDOFF.md)

## Current release state

The combined Windows release is published as [Windows build 6.1 · Pea Shoot Shrimp Dumpling · 豆苗蝦餃](https://github.com/cafepromenade/Windows-Server-Setupper/releases/tag/windows-6.1-49880c53). The release is non-draft, non-prerelease, targets commit [`49880c530e09ec9dc5e8030c747f464e72759acf`](https://github.com/cafepromenade/Windows-Server-Setupper/commit/49880c530e09ec9dc5e8030c747f464e72759acf), and was published at `2026-08-14T02:17:37Z` by the successful [Windows release workflow run 31763019082](https://github.com/cafepromenade/Windows-Server-Setupper/actions/runs/31763019082).

- [Download the unsigned Windows Server Tools installer](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-6.1-49880c53/WindowsServerTools-Setup-49880c530e09ec9dc5e8030c747f464e72759acf.exe) — `6,572,168` bytes.
- [Download the unsigned Exchange Auto Installer setup](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-6.1-49880c53/ExchangeAutoInstaller-1.6.1-x64-Setup.exe) — `142,329,856` bytes.
- [Open the public documentation site](https://cafepromenade.github.io/Windows-Server-Setupper/).
- [Open the owner-only documentation site](https://windows-server-setupper-guides.labapig.chatgpt.site).

This evidence proves publication and downloadable release assets for the named commit. It does not claim installer execution, a deployed Exchange environment, tests, lint, review, audit, or UI-capture evidence; those activities were not run in the ultra-speed release pass.

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
- The [Windows release contract](./docs/release/windows-release.md) records the build, package, and publication evidence for the verified release.
- The documentation-site route is `docs-site/build.bat /ci`. It creates the Cloudflare Worker-compatible `docs-site/dist` output and the GitHub Pages `docs-site/pages-dist` export without running its focused tests. The public site is live at [cafepromenade.github.io/Windows-Server-Setupper](https://cafepromenade.github.io/Windows-Server-Setupper/), and the owner-only site is live at [windows-server-setupper-guides.labapig.chatgpt.site](https://windows-server-setupper-guides.labapig.chatgpt.site).

<details>
<summary><strong>Local build and installer routes</strong></summary>

`build.bat /s` is the supported local runnable-application route. `build-installer.bat /s` is the supported local installer route. They build from the checkout but do not publish, tag, or create a release. Their outputs are generated artifacts and remain outside Git history.

This documentation-only update records the existing release evidence and does not rerun a build, package, installation, or publication command.

</details>

<details>
<summary><strong>Verified release evidence</strong></summary>

- **Tag:** `windows-6.1-49880c53`
- **Target:** `49880c530e09ec9dc5e8030c747f464e72759acf`
- **Release state:** non-draft and non-prerelease
- **Published:** `2026-08-14T02:17:37Z`
- **Workflow:** [run 31763019082](https://github.com/cafepromenade/Windows-Server-Setupper/actions/runs/31763019082), successful
- **Windows Server Tools installer:** `WindowsServerTools-Setup-49880c530e09ec9dc5e8030c747f464e72759acf.exe`, `6,572,168` bytes
- **Exchange setup:** `ExchangeAutoInstaller-1.6.1-x64-Setup.exe`, `142,329,856` bytes
- **Documentation:** [public GitHub Pages site](https://cafepromenade.github.io/Windows-Server-Setupper/) and [owner-only Sites deployment](https://windows-server-setupper-guides.labapig.chatgpt.site)

The installers are intentionally unsigned. This release record does not substitute for installation, runtime, test, lint, review, audit, or UI-capture evidence.

</details>

<details>
<summary><strong>Documentation map</strong></summary>

- [Release documentation](./docs/release/README.md)
- [Windows release contract](./docs/release/windows-release.md)
- [Dependency bootstrap inventory](./docs/release/dependency-bootstrap.md)
- [WPF reliability documentation](./Windows-Server-Tools/docs/reliability/README.md)
- [Exchange Auto Installer documentation](./Windows-Server-Tools/Exchange-Auto-Installer/README.md)

</details>
