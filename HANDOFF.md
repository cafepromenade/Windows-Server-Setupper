# Handoff: published combined Windows release

## Current release lookup

The 6.1 facts below are a historical baseline, not a statement of the current release. Before referring to a current published release, resolve [the repository's latest release](https://github.com/cafepromenade/Windows-Server-Setupper/releases/latest) and inspect that release record's tag, target commit, assets, workflow evidence, and publication time. This keeps the handoff accurate when a later push publishes a newer release.

## Historical release baseline (Windows 6.1)

At `2026-08-14T02:17:37Z`, [Windows build 6.1 · Pea Shoot Shrimp Dumpling · 豆苗蝦餃](https://github.com/cafepromenade/Windows-Server-Setupper/releases/tag/windows-6.1-49880c53) was a non-draft, non-prerelease release targeting commit [`49880c530e09ec9dc5e8030c747f464e72759acf`](https://github.com/cafepromenade/Windows-Server-Setupper/commit/49880c530e09ec9dc5e8030c747f464e72759acf), published by successful [workflow run 31763019082](https://github.com/cafepromenade/Windows-Server-Setupper/actions/runs/31763019082).

The 6.1 baseline release record covers:

- a WPF server-setup application with recovery-oriented state and explicit uncertain-outcome reconciliation;
- a mostly pre-filled Exchange installation plan that does not pre-fill credentials;
- the Cheap LFS Exchange media route, which validates release-part metadata before hydration and validates the final ISO before use;
- the optional managed OpenCode repair adviser, whose YOLO mode is off by default and limited to fixed Exchange repair actions; and
- the intentional unsigned boundaries for the WPF/Inno Setup and Exchange/Squirrel.Windows outputs.

## Verified historical delivery evidence (Windows 6.1)

- `build.bat /s` is the supported runnable-application route.
- `build-installer.bat /s` is the supported unsigned installer route for the WPF Inno Setup installer and Exchange Squirrel.Windows setup/update set.
- `.github/workflows/windows-release.yml` published the verified release through [run 31763019082](https://github.com/cafepromenade/Windows-Server-Setupper/actions/runs/31763019082). The workflow intentionally does not run tests or lint.
- The unsigned [Windows Server Tools installer](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-6.1-49880c53/WindowsServerTools-Setup-49880c530e09ec9dc5e8030c747f464e72759acf.exe) is `6,572,168` bytes.
- The unsigned [Exchange Auto Installer setup](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-6.1-49880c53/ExchangeAutoInstaller-1.6.1-x64-Setup.exe) is `142,329,856` bytes.
- The documentation is published at the [public GitHub Pages site](https://cafepromenade.github.io/Windows-Server-Setupper/) and the [owner-only Sites deployment](https://windows-server-setupper-guides.labapig.chatgpt.site).

## Evidence not produced by the 6.1 release pass

The ultra-speed pass did not run tests, lint, review, audit, installer execution, a live Exchange deployment, accessibility checks, or real built-artifact UI captures. The successful publication workflow is evidence of build, package, and publication only. Any future runtime or quality evidence must name the exact commit, environment, command, and result rather than inheriting a verdict from this release.

## Boundaries and next owner actions

- Use `windows-6.1-49880c53` and target `49880c530e09ec9dc5e8030c747f464e72759acf` only when citing the historical 6.1 baseline; resolve current-release facts from [the repository's latest release](https://github.com/cafepromenade/Windows-Server-Setupper/releases/latest). Previous recovery-only artifacts remain historical.
- Do not claim code signing: both installer families are intentionally unsigned, and a signer invocation is a release failure.
- Do not widen managed OpenCode repair into arbitrary execution. YOLO mode must remain explicit, off by default, and constrained to the application-defined repair catalog.
- Do not attach or hydrate the Exchange ISO as part of publication. The runtime media flow owns its validated download/reassembly path.
- On an appropriate test server, exercise both installers and record source-revision-specific installation and runtime evidence without converting an unrun check into a pass.
