# Exchange Auto Installer

Exchange Auto Installer is an unsigned Windows desktop application that guides a local administrator through a mostly pre-filled, staged Microsoft Exchange Server installation. It replaces the legacy fire-and-forget flow with durable stage records, bounded transient retries, explicit uncertain-outcome reconciliation, and a reachable retry or resume action after every stopped stage.

The app never pre-fills credentials. It accepts only local Exchange media selected through the native picker, records its SHA-256 digest, and requires a valid Microsoft Authenticode signature before enabling the installation path.

## What is pre-filled

- Mailbox role
- Standard Exchange installation directory
- First mailbox database name and local paths
- The fixed Windows Server feature list
- Schema, organization, and all-domain preparation stages
- Diagnostic data disabled
- Two bounded retries for a documented transient installer exit

The detected Active Directory domain is suggested only after it is read from the local server. The license acknowledgement remains unselected, and no password, token, key, or credential is stored in the profile.

## Recovery model

Every stage is written atomically to application data before it starts and after it stops. A clean exit code marks the stage complete. A timeout, cancellation, missing process result, or app interruption marks the stage uncertain instead of guessing that it failed or succeeded. Completed stages remain complete across restarts.

Cancellation is honored only between stages. The app does not terminate Exchange Setup at an arbitrary point. Process output is stored locally as bounded JSON Lines after sensitive-looking assignments and private paths are redacted.

## OpenCode installation fixer

The optional fixer installs a pinned official OpenCode CLI build, verifies its published digest, and runs it only against a bounded, redacted diagnostic workspace. See [OpenCode repair and YOLO mode](docs/opencode-repair.md) for its exact limits.

YOLO mode is off by default. It applies only to the fixed Exchange repair action catalog produced by this application; it cannot approve arbitrary commands, credentials, unrelated directories, policy bypasses, or hidden network destinations.

## Build and package

From `Windows-Server-Tools/Exchange-Auto-Installer`:

```powershell
npm ci
npm run build
npm run package
```

`npm run build` produces an unpacked application. `npm run package` produces an unsigned Squirrel.Windows installer and update files under `dist/squirrel-windows/`. No signing certificate is discovered or used. Windows may show an unknown-publisher warning.

Expected package outputs include:

- `ExchangeAutoInstaller-1.0.0-x64-Setup.exe`
- `RELEASES`
- a full `.nupkg`

## Safety boundaries

- Electron renderer isolation is enabled (`contextIsolation`, sandbox, and no Node.js integration).
- IPC operations are fixed structured methods; there is no command prompt or arbitrary shell field.
- Child processes use executable-plus-argument arrays with shell expansion disabled.
- Exchange media is re-verified immediately before every Exchange Setup stage.
- The installer refuses to begin without elevation, a proven Active Directory domain, a clear restart state, and verified local media.
- OpenCode receives only a bounded redacted diagnostic bundle and cannot directly mutate the server.

## Verification note

This ultra-speed delivery intentionally does not run tests, linters, runtime UI checks, or captures. Build and packaging commands only prove that an artifact was produced; they do not prove that an Exchange installation completed successfully on a server.
