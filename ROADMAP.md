# Roadmap

## Current milestone: operate and validate the published combined Windows release

The current source describes two complementary Windows applications and their delivery routes:

- the resilient WPF server-setup application with recovery-oriented state handling;
- the mostly pre-filled, staged Exchange Auto Installer;
- Cheap LFS metadata verification and explicit ISO hydration for Exchange media, with no standard Git LFS route;
- optional managed OpenCode repair with an explicit, off-by-default YOLO mode constrained to the application's repair catalog;
- unsigned WPF/Inno Setup and Exchange/Squirrel.Windows artifact routes; and
- a documentation-site build route that creates `docs-site/dist` for Sites and `docs-site/pages-dist` for GitHub Pages.

The combined release is published as [Windows build 8.1 · Dried Scallop Shrimp Dumpling · 瑤柱蝦餃](https://github.com/cafepromenade/Windows-Server-Setupper/releases/tag/windows-8.1-ba3d587a). It targets commit [`ba3d587a6b1240d960ea390a43b6c8928e521ff1`](https://github.com/cafepromenade/Windows-Server-Setupper/commit/ba3d587a6b1240d960ea390a43b6c8928e521ff1), is non-draft and non-prerelease, and was published at `2026-08-14T05:37:03Z` by successful [workflow run 31773190945](https://github.com/cafepromenade/Windows-Server-Setupper/actions/runs/31773190945). Its release-record publication interval is `2026-08-14T05:30:14Z` to `2026-08-14T05:37:03Z` (`00:06:49`). The [public GitHub Pages site](https://cafepromenade.github.io/Windows-Server-Setupper/) and [owner-only Sites deployment](https://windows-server-setupper-guides.labapig.chatgpt.site) are live.

## Published delivery evidence

1. The unsigned [Windows Server Tools installer](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/WindowsServerTools-Setup-ba3d587a6b1240d960ea390a43b6c8928e521ff1.exe) is published at `6,572,044` bytes with SHA-256 `3e3e72e125671736df93661067e01c42d644f6c75f01b7053e1aafb7dff032c1`.
2. The unsigned [Exchange Auto Installer setup](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/ExchangeAutoInstaller-1.8.1-x64-Setup.exe) is published at `142,329,856` bytes with SHA-256 `a5d40df90018ed6ba2ea15e26612c8353189b93c61512f23210e3cc91446d800`.
3. The Exchange update set includes [`RELEASES`](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/RELEASES) at `94` bytes with SHA-256 `a2c276e594eafb206949b83958184d7e5e46442fc9a5a2f674b138c32fecb8bc` and [`exchange-auto-installer-1.8.1-full.nupkg`](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/exchange-auto-installer-1.8.1-full.nupkg) at `141,268,448` bytes with SHA-256 `6ca88065dd39820538b84f794b3f19e42b4a812fd040f4b26b77c037220e8b31`.
4. The GitHub release and both documentation deployments are published and linked above.
5. The Exchange ISO remains outside release assets and Git history. Runtime media use must still validate the Cheap LFS part inventory and final ISO before installation.

## Next evidence milestones

1. Exercise both unsigned installers on an appropriate Windows test server and record the exact release, environment, and outcome.
2. Exercise a non-production Exchange installation through the guided plan, including stop, retry, resume, media verification, and bounded repair behavior.
3. Run and record any requested local tests, lint, review, audit, accessibility checks, and real built-artifact UI captures against an exact commit. None of these were run by the ultra-speed release pass.
4. Keep release, documentation, and handoff records tied to immutable tags, assets, and commits as later releases supersede this one.

## Safety and evidence boundaries

- Both artifact families are intentionally unsigned. Unknown-publisher and SmartScreen warnings are expected; no release record may claim a signing result.
- The Exchange installer must not pre-fill credentials. Its OpenCode repair path remains managed and bounded; YOLO mode must remain opt-in and confined to fixed repair actions.
- The GitHub Actions release workflow is designed to build and publish rather than run tests or lint. Any local test, lint, review, audit, runtime, or UI-capture evidence must identify its exact source revision and must not be inferred from the successful publication workflow.
- Earlier recovery-only WPF releases and historical local checks remain historical records; the current publication evidence is the immutable `windows-8.1-ba3d587a` release named above.

See [release documentation](./docs/release/README.md) for the delivery contract and [HANDOFF.md](./HANDOFF.md) for the next owner actions.
