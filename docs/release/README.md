# Release documentation

This category records the Windows-only build, packaging, and publication route for the resilient WPF server tools and the Exchange Auto Installer. For live release facts and downloads, resolve [the repository's latest release](https://github.com/cafepromenade/Windows-Server-Setupper/releases/latest). [Windows build 8.1 · Dried Scallop Shrimp Dumpling · 瑤柱蝦餃](https://github.com/cafepromenade/Windows-Server-Setupper/releases/tag/windows-8.1-ba3d587a) is the verified immutable baseline, published from commit `ba3d587a6b1240d960ea390a43b6c8928e521ff1` at `2026-08-14T05:37:03Z`.

- [Windows release contract](./windows-release.md)
- [Dependency bootstrap inventory](./dependency-bootstrap.md)
- [Current handoff](../../HANDOFF.md)

The successful [Windows 8.1 baseline publication run](https://github.com/cafepromenade/Windows-Server-Setupper/actions/runs/31773190945) created one non-draft, non-prerelease release on 2026-08-14 at 05:37:03 UTC. Its release-record publication interval is `2026-08-14T05:30:14Z` to `2026-08-14T05:37:03Z` (`00:06:49`). The workflow built and published artifacts without tests, lint, type checking, static analysis, accessibility checks, reviews, audits, installer execution, or screenshots; those activities must not be inferred from the successful publication result.

## Verified immutable Windows 8.1 baseline delivery assets

| Role | Asset | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| Primary WPF installer | [`WindowsServerTools-Setup-ba3d587a6b1240d960ea390a43b6c8928e521ff1.exe`](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/WindowsServerTools-Setup-ba3d587a6b1240d960ea390a43b6c8928e521ff1.exe) | 6572044 | `3e3e72e125671736df93661067e01c42d644f6c75f01b7053e1aafb7dff032c1` |
| Exchange Squirrel setup | [`ExchangeAutoInstaller-1.8.1-x64-Setup.exe`](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/ExchangeAutoInstaller-1.8.1-x64-Setup.exe) | 142329856 | `a5d40df90018ed6ba2ea15e26612c8353189b93c61512f23210e3cc91446d800` |
| Exchange Squirrel index | [`RELEASES`](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/RELEASES) | 94 | `a2c276e594eafb206949b83958184d7e5e46442fc9a5a2f674b138c32fecb8bc` |
| Exchange Squirrel full package | [`exchange-auto-installer-1.8.1-full.nupkg`](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/exchange-auto-installer-1.8.1-full.nupkg) | 141268448 | `6ca88065dd39820538b84f794b3f19e42b4a812fd040f4b26b77c037220e8b31` |

The [public documentation site](https://cafepromenade.github.io/Windows-Server-Setupper/) and the [owner-only Sites deployment](https://windows-server-setupper-guides.labapig.chatgpt.site) are live. The source route `docs-site/build.bat /ci` emits `docs-site/pages-dist` for the `/Windows-Server-Setupper/` base path while producing `docs-site/dist` for Sites.
