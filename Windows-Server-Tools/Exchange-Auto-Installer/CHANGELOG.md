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
- Original vector logo, deterministic seven-size Windows icon, and packaged icon application without code signing.
- Tabbed local settings/tools with bounded personal vocabulary, search and regex construction, command palette, appearance/narration preferences, honest converter catalog, loopback Ollama status, offline documentation, and Squirrel update states.
- Cheap LFS Exchange ISO metadata verification and explicit hydration with pinned whole-object size and SHA-256.

### Security

- Blocked duplicate privileged processes with a protected state lease and monotonic compare-and-swap revisions.
- Preserved corrupt primary/backup state instead of silently replacing it with a fresh plan.
- Blocked a second privileged Setup launch after indeterminate outcomes until one-use reviewed reconciliation succeeds.
- Rejected network, UNC, device, relative, missing, and redirected media paths; required Exchange product/layout evidence and stable media identity.
- Stopped the pipeline for restart-required success codes and required a new boot plus fresh preflight.
- Verified Windows feature completion instead of trusting process exit alone, and honored cancellation during transient retry backoff.
- Corrected the pinned OpenCode asset to its official matching name, byte count, and SHA-256 metadata.
- Isolated OpenCode configuration/profile/plugin roots and reduced its provider-bound diagnostic schema.
- Extended redaction across authorization headers, bearer values, quoted assignments, PowerShell flags, and camel/snake secret keys.
