# OpenCode repair and bounded YOLO mode

The optional Exchange installation fixer uses OpenCode only as a read-only adviser. It cannot execute a command, edit a file, read outside its temporary diagnostic workspace, use the network, ask a subagent, or approve an action by itself.

## Pinned official package

- Version: `1.18.18`
- Official release: `https://github.com/anomalyco/opencode/releases/tag/v1.18.18`
- Windows x64 asset: `opencode-windows-x64.zip`
- Asset SHA-256: `66ad3d31bdc48d7cf16e212da21449bdfe34656cf83a56f682a211c8b78d30ba`

The app downloads only the fixed official URL over HTTPS, follows at most five redirects through GitHub's known release hosts, caps the response at 200 MiB, verifies the SHA-256 before extraction, and verifies the extracted CLI reports version `1.18.18`. The managed executable is rehashed before use. A missing, different, or corrupt installation is reported and can be replaced through the explicit **Install or repair OpenCode** action.

## Invocation contract

The app invokes OpenCode with an executable and an argument array, never a shell string:

```text
opencode.exe run <fixed repair objective> --format json --agent plan --dir <isolated diagnostic workspace>
```

The flags are documented by the official OpenCode CLI reference. The app deliberately does not pass `--auto`. A per-run `opencode.json` denies every tool by default, allows only reads inside the isolated workspace, and explicitly denies edits, shell commands, tasks, external directories, web access, questions, skills, and language-server actions.

The diagnostic bundle contains only current stage identifiers, statuses, attempt counts, exit codes, bounded error summaries, reconciliation summaries, preflight state, and restart state. Credentials, tokens, environment values, media paths, application-data paths, and user-profile paths are omitted or redacted.

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

This ultra-speed delivery intentionally did not run tests, lint, runtime UI checks, or captures. Packaging is not proof that OpenCode can reach a configured model provider or that a proposed repair will complete an Exchange installation.
