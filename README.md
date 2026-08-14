# Windows Server Setupper

Windows Server Setupper contains two Windows administration applications: a resilient .NET Framework 4.7.2 WPF server-setup application and the Exchange Auto Installer. Both can change server roles, network settings, scheduled tasks, directory-service data, and Exchange configuration; use them only on an appropriate test server and review each requested operation.

## Index

- [Latest release and verified baseline](#latest-release-and-verified-baseline)
- [Applications](#applications)
- [Delivery and documentation](#delivery-and-documentation)
- [Roadmap](./ROADMAP.md) and [handoff](./HANDOFF.md)

## Latest release and verified baseline

For the current release and its downloadable assets, use [the repository's latest release](https://github.com/cafepromenade/Windows-Server-Setupper/releases/latest). The Windows 8.1 record below is a verified immutable baseline, not a claim about whichever release is latest when this page is read.

The verified immutable baseline is [Windows build 8.1 · Dried Scallop Shrimp Dumpling · 瑤柱蝦餃](https://github.com/cafepromenade/Windows-Server-Setupper/releases/tag/windows-8.1-ba3d587a). It is non-draft, non-prerelease, targets commit [`ba3d587a6b1240d960ea390a43b6c8928e521ff1`](https://github.com/cafepromenade/Windows-Server-Setupper/commit/ba3d587a6b1240d960ea390a43b6c8928e521ff1), and was published at `2026-08-14T05:37:03Z` by the successful [Windows release workflow run 31773190945](https://github.com/cafepromenade/Windows-Server-Setupper/actions/runs/31773190945). The release-record publication interval began at `2026-08-14T05:30:14Z` and ended at `2026-08-14T05:37:03Z` (`00:06:49`).

- **Verified Windows 8.1 baseline asset:** [unsigned Windows Server Tools installer](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/WindowsServerTools-Setup-ba3d587a6b1240d960ea390a43b6c8928e521ff1.exe) — `6,572,044` bytes; SHA-256 `3e3e72e125671736df93661067e01c42d644f6c75f01b7053e1aafb7dff032c1`.
- **Verified Windows 8.1 baseline asset:** [unsigned Exchange Auto Installer setup](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/ExchangeAutoInstaller-1.8.1-x64-Setup.exe) — `142,329,856` bytes; SHA-256 `a5d40df90018ed6ba2ea15e26612c8353189b93c61512f23210e3cc91446d800`.
- **Verified Windows 8.1 baseline asset:** [Exchange Squirrel update index](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/RELEASES) — `94` bytes; SHA-256 `a2c276e594eafb206949b83958184d7e5e46442fc9a5a2f674b138c32fecb8bc`.
- **Verified Windows 8.1 baseline asset:** [Exchange Squirrel full package](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/exchange-auto-installer-1.8.1-full.nupkg) — `141,268,448` bytes; SHA-256 `6ca88065dd39820538b84f794b3f19e42b4a812fd040f4b26b77c037220e8b31`.
- [Open the public documentation site](https://cafepromenade.github.io/Windows-Server-Setupper/).
- [Open the owner-only documentation site](https://windows-server-setupper-guides.labapig.chatgpt.site).

This baseline evidence proves publication and downloadable release assets for the named commit. It does not claim installer execution, a deployed Exchange environment, tests, lint, review, audit, or UI-capture evidence; those activities were not run in the ultra-speed release pass.

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
- The [Windows release contract](./docs/release/windows-release.md) records the verified immutable baseline and directs live-release readers to the latest release record.
- The documentation-site route is `docs-site/build.bat /ci`. It creates the Cloudflare Worker-compatible `docs-site/dist` output and the GitHub Pages `docs-site/pages-dist` export without running its focused tests. The public site is live at [cafepromenade.github.io/Windows-Server-Setupper](https://cafepromenade.github.io/Windows-Server-Setupper/), and the owner-only site is live at [windows-server-setupper-guides.labapig.chatgpt.site](https://windows-server-setupper-guides.labapig.chatgpt.site).

<details>
<summary><strong>Local build and installer routes</strong></summary>

`build.bat /s` is the supported local runnable-application route. `build-installer.bat /s` is the supported local installer route. They build from the checkout but do not publish, tag, or create a release. Their outputs are generated artifacts and remain outside Git history.

This documentation-only update records the existing release evidence and does not rerun a build, package, installation, or publication command.

</details>

<details>
<summary><strong>Verified immutable Windows 8.1 baseline evidence</strong></summary>

- **Latest release:** [Open the repository's latest release](https://github.com/cafepromenade/Windows-Server-Setupper/releases/latest)
- **Baseline release:** [Windows build 8.1 · Dried Scallop Shrimp Dumpling · 瑤柱蝦餃](https://github.com/cafepromenade/Windows-Server-Setupper/releases/tag/windows-8.1-ba3d587a)
- **Tag:** `windows-8.1-ba3d587a`
- **Target:** `ba3d587a6b1240d960ea390a43b6c8928e521ff1`
- **Release state:** non-draft and non-prerelease
- **Published:** `2026-08-14T05:37:03Z`
- **Workflow:** [run 31773190945](https://github.com/cafepromenade/Windows-Server-Setupper/actions/runs/31773190945), completed successfully
- **Release-record timing:** `2026-08-14T05:30:14Z` to `2026-08-14T05:37:03Z` (`00:06:49`)
- **Windows Server Tools installer:** `WindowsServerTools-Setup-ba3d587a6b1240d960ea390a43b6c8928e521ff1.exe`, `6,572,044` bytes, SHA-256 `3e3e72e125671736df93661067e01c42d644f6c75f01b7053e1aafb7dff032c1`
- **Exchange setup:** `ExchangeAutoInstaller-1.8.1-x64-Setup.exe`, `142,329,856` bytes, SHA-256 `a5d40df90018ed6ba2ea15e26612c8353189b93c61512f23210e3cc91446d800`
- **Exchange update index:** `RELEASES`, `94` bytes, SHA-256 `a2c276e594eafb206949b83958184d7e5e46442fc9a5a2f674b138c32fecb8bc`
- **Exchange full package:** `exchange-auto-installer-1.8.1-full.nupkg`, `141,268,448` bytes, SHA-256 `6ca88065dd39820538b84f794b3f19e42b4a812fd040f4b26b77c037220e8b31`
- **Documentation:** [public GitHub Pages site](https://cafepromenade.github.io/Windows-Server-Setupper/) and [owner-only Sites deployment](https://windows-server-setupper-guides.labapig.chatgpt.site)

The installers are intentionally unsigned. This baseline record does not substitute for installation, runtime, test, lint, review, audit, or UI-capture evidence.

</details>

<details>
<summary><strong>Documentation map</strong></summary>

- [Release documentation](./docs/release/README.md)
- [Windows release contract](./docs/release/windows-release.md)
- [Dependency bootstrap inventory](./docs/release/dependency-bootstrap.md)
- [WPF reliability documentation](./Windows-Server-Tools/docs/reliability/README.md)
- [Exchange Auto Installer documentation](./Windows-Server-Tools/Exchange-Auto-Installer/README.md)

</details>
