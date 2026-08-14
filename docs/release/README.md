# Release documentation

This category records the Windows-only build, packaging, and publication route for the resilient WPF server tools and the Exchange Auto Installer. It describes a pending combined release; it does not assert a current final package, release, site deployment, or verification result.

- [Windows release contract](./windows-release.md)
- [Dependency bootstrap inventory](./dependency-bootstrap.md)
- [Current handoff](../../HANDOFF.md)

The configured Windows workflow builds and publishes artifacts without tests, lint, type checking, static analysis, accessibility checks, or screenshots. That workflow behavior is not evidence that a final combined release has run.

The source also contains a GitHub Pages-ready documentation-site route: `docs-site/build.bat /ci` emits `docs-site/pages-dist` for the `/Windows-Server-Setupper/` base path while producing `docs-site/dist` for Sites. Publication and a final URL remain pending until the exact export is uploaded and verified.
