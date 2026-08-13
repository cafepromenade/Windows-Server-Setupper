# OpenCode repair and bounded YOLO mode

The optional Exchange installation fixer uses OpenCode only as a read-only adviser. It cannot execute a command, edit a file, read outside its temporary diagnostic workspace, ask a subagent, or approve an action by itself. A configured model provider can receive the neutral diagnostic schema when the administrator explicitly starts advice; the app does not claim that provider inference is offline. Hostname, domain, media path, environment values, credentials, raw logs, and arbitrary detail are omitted.

## Pinned official package

- Version: `1.18.18`
- Official release: `https://github.com/anomalyco/opencode/releases/tag/v1.18.18`
- Windows x64 asset: `opencode-windows-x64.zip`
- Asset size: `60,504,740` bytes
- Asset SHA-256: `c6d265376fdb93164013671b0cf402410184f73c34fc15d82d40a16a745b15f4`

The app downloads only the fixed official URL over HTTPS, follows at most five redirects through GitHub's known release hosts, requires the exact published byte count, caps the response at 200 MiB, verifies the SHA-256 before extraction, and verifies the extracted CLI reports version `1.18.18`. The managed executable is rehashed before use. A missing, different, or corrupt installation is reported and can be replaced through the explicit **Install or repair OpenCode** action. `npm run verify:opencode-release` independently compares the committed name, size, and digest with the official release metadata through the GitHub CLI.

## Invocation contract

The app invokes OpenCode with an executable and an argument array, never a shell string:

```text
opencode.exe run <fixed repair objective> --format json --agent plan --dir <isolated diagnostic workspace>
```

The flags are documented by the official OpenCode CLI reference. The app deliberately does not pass `--auto`. A per-run `opencode.json` denies every tool by default, allows only reads inside the isolated workspace, and explicitly denies edits, shell commands, tasks, external directories, web access, questions, skills, and language-server actions. Each run uses isolated `HOME`, `USERPROFILE`, `APPDATA`, `LOCALAPPDATA`, and XDG paths and disables global/project configuration and plugin discovery.

The diagnostic bundle contains only current stage identifiers, statuses, attempt counts, exit codes, error categories, reconciliation summaries, bounded preflight check identifiers/statuses, and restart state. Credentials, tokens, environment values, hostnames, domains, media paths, raw logs, application-data paths, and user-profile paths are omitted.

Runs have a ten-minute timeout, stream redacted progress, support emergency stop, and terminate the process tree if cancellation or timeout requires it. A nonzero exit, timeout, cancellation, malformed JSON result, or unknown action ID is an explicit stopped result.

## Repair action catalog

OpenCode may select only these application-owned action IDs:

- `reinspect_media`
- `refresh_preflight`
- `retry_failed_stage`
- `resume_installation`
- `export_redacted_logs`

OpenCode never executes those actions. The Electron main process validates the plan identity, validates each selected ID against the current plan, and routes it to the existing structured installer operation.

## YOLO mode

YOLO mode is off by default. Enabling it requires typing the exact acknowledgement `ENABLE BOUNDED YOLO`. It may auto-approve only the current app-generated action IDs listed above. It does not approve arbitrary commands, credential access, unrelated directories, unbounded destructive actions, policy bypasses, or hidden network destinations. Emergency stop remains available while the adviser is running.

Manual review remains the default. In manual mode, the app displays the returned action IDs and requires an explicit apply action before routing them. The preference is stored only in private local application data and can be turned off at any time.

## Verification boundary

Focused checks validate the pinned official release metadata and isolation/redaction boundaries. Packaging is not proof that OpenCode can reach a configured model provider or that a proposed repair will complete an Exchange installation. Real provider interaction and capture evidence remain release blockers in the universal inventory.
