# Automatic updates

## Behavior

The WPF application checks a configured HTTPS JSON manifest after the application shell is usable, every six hours while it remains open, and whenever the operator selects **Check for updates**. Checks time out after 15 seconds and never block access to server tools.

When a newer version exists, the installer downloads in the background. The application requires the exact byte length and SHA-256 declared by the manifest before atomically promoting the temporary file into protected machine-wide staging. The status surface exposes real progress and cancellation. A cancelled, short, oversized, corrupt, hash-mismatched, or unsupported package is removed and never offered for execution.

The ready notification names the exact version and states that the installer is unsigned. Installation begins only after the operator chooses **Restart to install update**. **Later** keeps the current application running. Restart remains unavailable while a server mutation is active or while the domain, password, or software fields contain unsaved values.

## Manifest contract

The manifest is UTF-8 JSON, at most 128 KiB, with schema version `1` and exactly these fields:

| Field | Meaning |
|---|---|
| `schemaVersion` | Must equal `1`. |
| `version` | A parseable four-part application version. |
| `releaseNotesUrl` | Absolute public HTTPS URL without embedded credentials. |
| `assetUrl` | Absolute public HTTPS installer URL without embedded credentials. |
| `sha256` | Exactly 64 hexadecimal characters. |
| `sizeBytes` | Positive installer length, capped at 1 GiB. |

Duplicate properties, unknown properties, unsupported versions, loopback URLs, credentials in URLs, non-HTTPS URLs, and manifest redirects are rejected. Installer delivery may follow at most three HTTPS redirects; the original manifest hash and size remain authoritative after every redirect.

The configured feed is in `App.config`. The committed `update-manifest.json` is the publication source and must be updated only after its installer asset, byte length, digest, version, and release notes are final.

## Persistence and rollback

Verified staging and the version-transition record live below the protected common application-data root. The record is written only after package verification and records when an installer launch was attempted. On the next launch:

- a current or newer installed version clears obsolete staging;
- a missing or changed staged package is discarded;
- an attempted update whose target version is not installed is reported as incomplete, while the prior installed application remains active and the verified package can be retried.

The application never overwrites its own running executable. If the external installer cannot start or finish, the prior installation remains the rollback state.

## Failure modes and security boundaries

- Offline, timeout, and HTTP failures produce a persistent non-blocking failure state with a later manual retry.
- Invalid metadata and hash or size failures are explicit and never become a ready state.
- Runtime transport uses the platform HTTPS certificate validation and sends no credentials or cookies.
- The updater never invokes a signer and never claims signature verification. SHA-256 proves that downloaded bytes match the published manifest, not publisher identity.
- The installer is rehashed immediately before launch. Active server work and unsaved form values prevent launch.

## Verification

`Windows-Server-Tools.Tests` covers current and available versions, strict schema validation, duplicate and unknown fields, HTTPS enforcement, refused manifest redirects, bounded HTTPS package redirects, offline failure, exact progress, atomic staging, cancellation cleanup, hash mismatch cleanup, corrupt staged-package refusal, persisted ready state, attempted-install rollback diagnosis, and source contracts for startup, schedule, manual check, non-blocking status, and unsaved-work protection.

Suggested articles: [Reviewed initial server setup](reviewed-initial-server-setup.md), [Error recovery](error-recovery.md), and [WPF completeness](../completeness/wpf-universal-feature-inventory.md).
