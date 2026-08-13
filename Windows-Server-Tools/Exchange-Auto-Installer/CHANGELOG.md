# Changelog

## 1.0.0 - 2026-08-13

### Added

- Mostly pre-filled guided profile for a local Microsoft Exchange Server installation.
- Native local-media selection with SHA-256 and Microsoft Authenticode validation.
- Fail-closed preflight for elevation, Active Directory state, pending restart, organization values, and media integrity.
- Durable staged installation for Windows features, schema preparation, organization preparation, all-domain preparation, Mailbox role installation, and postflight inspection.
- Bounded retry for documented transient installer contention and explicit uncertain states for interrupted processes.
- Safe-boundary cancellation, restart-safe resume, per-stage retry, and redacted local logs.
- Optional pinned OpenCode bootstrap and read-only repair adviser with explicit, off-by-default bounded YOLO mode.
- Unsigned Squirrel.Windows packaging with signing disabled.
