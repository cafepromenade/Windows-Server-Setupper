# Release documentation

This category records the Windows-only build, packaging, and publication route for the resilient WPF server tools and the Exchange Auto Installer. The current verified release is [Windows build 6.1 · Pea Shoot Shrimp Dumpling · 豆苗蝦餃](https://github.com/cafepromenade/Windows-Server-Setupper/releases/tag/windows-6.1-49880c53), published from commit `49880c530e09ec9dc5e8030c747f464e72759acf`.

- [Windows release contract](./windows-release.md)
- [Dependency bootstrap inventory](./dependency-bootstrap.md)
- [Current handoff](../../HANDOFF.md)

The successful [publication run](https://github.com/cafepromenade/Windows-Server-Setupper/actions/runs/31763019082) created one non-draft, non-prerelease release on 2026-08-14 at 02:17:37 UTC. The workflow built and published artifacts without tests, lint, type checking, static analysis, accessibility checks, reviews, audits, installer execution, or screenshots; those activities must not be inferred from the successful publication result.

The [public documentation site](https://cafepromenade.github.io/Windows-Server-Setupper/) and the [owner-only Sites deployment](https://windows-server-setupper-guides.labapig.chatgpt.site) are live. The source route `docs-site/build.bat /ci` emits `docs-site/pages-dist` for the `/Windows-Server-Setupper/` base path while producing `docs-site/dist` for Sites.
