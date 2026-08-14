# Roadmap

## Current milestone: operate and validate the published combined Windows release

The current source describes two complementary Windows applications and their delivery routes:

- the resilient WPF server-setup application with recovery-oriented state handling;
- the mostly pre-filled, staged Exchange Auto Installer;
- Cheap LFS metadata verification and explicit ISO hydration for Exchange media, with no standard Git LFS route;
- optional managed OpenCode repair with an explicit, off-by-default YOLO mode constrained to the application's repair catalog;
- unsigned WPF/Inno Setup and Exchange/Squirrel.Windows artifact routes; and
- a documentation-site build route that creates `docs-site/dist` for Sites and `docs-site/pages-dist` for GitHub Pages.

The combined release is published as [Windows build 6.1 · Pea Shoot Shrimp Dumpling · 豆苗蝦餃](https://github.com/cafepromenade/Windows-Server-Setupper/releases/tag/windows-6.1-49880c53). It targets commit [`49880c530e09ec9dc5e8030c747f464e72759acf`](https://github.com/cafepromenade/Windows-Server-Setupper/commit/49880c530e09ec9dc5e8030c747f464e72759acf), is non-draft and non-prerelease, and was published at `2026-08-14T02:17:37Z` by successful [workflow run 31763019082](https://github.com/cafepromenade/Windows-Server-Setupper/actions/runs/31763019082). The [public GitHub Pages site](https://cafepromenade.github.io/Windows-Server-Setupper/) and [owner-only Sites deployment](https://windows-server-setupper-guides.labapig.chatgpt.site) are live.

## Published delivery evidence

1. The unsigned [Windows Server Tools installer](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-6.1-49880c53/WindowsServerTools-Setup-49880c530e09ec9dc5e8030c747f464e72759acf.exe) is published at `6,572,168` bytes.
2. The unsigned [Exchange Auto Installer setup](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-6.1-49880c53/ExchangeAutoInstaller-1.6.1-x64-Setup.exe) is published at `142,329,856` bytes.
3. The GitHub release and both documentation deployments are published and linked above.
4. The Exchange ISO remains outside release assets and Git history. Runtime media use must still validate the Cheap LFS part inventory and final ISO before installation.

## Next evidence milestones

1. Exercise both unsigned installers on an appropriate Windows test server and record the exact release, environment, and outcome.
2. Exercise a non-production Exchange installation through the guided plan, including stop, retry, resume, media verification, and bounded repair behavior.
3. Run and record any requested local tests, lint, review, audit, accessibility checks, and real built-artifact UI captures against an exact commit. None of these were run by the ultra-speed release pass.
4. Keep release, documentation, and handoff records tied to immutable tags, assets, and commits as later releases supersede this one.

## Safety and evidence boundaries

- Both artifact families are intentionally unsigned. Unknown-publisher and SmartScreen warnings are expected; no release record may claim a signing result.
- The Exchange installer must not pre-fill credentials. Its OpenCode repair path remains managed and bounded; YOLO mode must remain opt-in and confined to fixed repair actions.
- The GitHub Actions release workflow is designed to build and publish rather than run tests or lint. Any local test, lint, review, audit, runtime, or UI-capture evidence must identify its exact source revision and must not be inferred from the successful publication workflow.
- Earlier recovery-only WPF releases and historical local checks remain historical records; the current publication evidence is the immutable `windows-6.1-49880c53` release named above.

See [release documentation](./docs/release/README.md) for the delivery contract and [HANDOFF.md](./HANDOFF.md) for the next owner actions.
