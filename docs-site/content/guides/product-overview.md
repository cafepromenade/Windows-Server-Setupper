# Windows Server Setupper overview
Category: Product
Suggested: resilient-recovery,releases-changelog-and-downloads,build-and-installer-route

## What it is

Windows Server Setupper is a collection of Windows desktop tools for configuring server roles, baseline settings, directory services, shared folders, and selected software. The primary application is a .NET Framework 4.7.2 WPF desktop application.

## Operating boundary

The tools can change operating-system roles, network settings, security settings, scheduled tasks, and directory-service data. Evaluate them on an appropriate test server, review each requested operation, and use administrative rights only when the operation requires them.

## Current release

For the currently published release and its current download choices, use [GitHub’s latest release record](https://github.com/cafepromenade/Windows-Server-Setupper/releases/latest). That stable URL follows newer published releases without making this overview chase a moving tag.

## Verified immutable baseline

[Windows build 8.1 · Dried Scallop Shrimp Dumpling · 瑤柱蝦餃](https://github.com/cafepromenade/Windows-Server-Setupper/releases/tag/windows-8.1-ba3d587a) was published on 2026-08-14 at 05:37:03 UTC as tag `windows-8.1-ba3d587a` from commit `ba3d587a6b1240d960ea390a43b6c8928e521ff1`. It is preserved as verified immutable baseline evidence, not as a claim that it remains the latest release. Its WPF and Exchange installers are intentionally unsigned; the release record preserves every attached asset's exact size and SHA-256 digest, workflow timing, and the checks that were and were not run.
