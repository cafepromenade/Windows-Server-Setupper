# Handoff: error-recovery hardening

## Status

The error-recovery implementation is present in the current working copy. A completed local verification baseline exists, but source review continued afterward; the final focused checks and WPF compile remain with the integrating owner after source freeze. Default-branch reconciliation, commit creation, remote publication, remote workflow evidence, installer creation, and release evidence also remain pending.

This handoff deliberately makes no claim that the current source is committed, pushed, released, or deployed.

## Implemented behavior

- Durable `windows-server-tools-recovery-v3` checkpoints preserve completed work and distinguish pending, running, retrying, failed, succeeded, blocked, and indeterminate states.
- Checkpoints live in protected per-machine application data, use bounded canonical records plus a SHA-256 commit record, and are serialized by a bounded cross-process lease.
- Automatic retries are both bounded and limited to operations declared idempotent. Explicit retry uses expected state/generation/attempt comparisons so stale UI actions cannot reset newer state.
- User-reviewed reconciliation is atomic and supports two separate uncertain outcomes: confirmed succeeded, or confirmed stopped without completing/applying.
- Corrupt state blocks replay. Its explicit repair path archives and verifies the evidence before creating an empty checkpoint.
- External processes run in kill-on-close Job Objects. The caller verifies initial exit, child-process completion, exit code, timeout termination, and whether termination was actually confirmed.
- The WPF recovery card retains multiple pending actions, precise Retry/completed choices, a persistent review route, accessible live announcements, explanatory disabled states, and focus restoration.
- Completion cleanup is independently retryable and does not repeat completed server actions.
- Chocolatey 2.7.3 installation uses a pinned package and installer digest inside protected staging and verifies the installed version.
- Legacy server-role controls that reach the Exchange/SCCM delivery chain remain disabled until credential-safe replacement artifacts are published and pinned.

The detailed contract is in [Error recovery and resumable operations](./Windows-Server-Tools/docs/reliability/error-recovery.md).

## Local verification evidence

| Verification | Result |
| --- | --- |
| Focused recovery executable | Latest completed baseline: `PASS: 146 recovery checks`; final rerun pending |
| Fresh primary WPF scratch build | Baseline succeeded and produced `Windows-Server-Tools.exe`; final source-freeze rerun pending |
| Scratch/source identity | SHA-256 matched the recovery engine, WPF XAML/code-behind, functions, and application startup source at the baseline; final comparison pending |
| Targeting environment | Scratch .NET Framework 4.7.2 reference assemblies because the host developer targeting pack was unavailable |

Source review continued after the 146-check baseline. Re-run the checks, source-hash comparison, and WPF compile after the final source freeze, and report the final integrated evidence rather than assuming this baseline is unchanged.

Reproducible commands and the exact evidence boundary are recorded in the [verification section](./Windows-Server-Tools/docs/reliability/error-recovery.md#local-verification).

## Primary implementation files

```text
Windows-Server-Tools/Windows-Server-Tools/Recovery.cs
Windows-Server-Tools/Windows-Server-Tools/App.xaml.cs
Windows-Server-Tools/Windows-Server-Tools/Functions.cs
Windows-Server-Tools/Windows-Server-Tools/MainWindow.xaml
Windows-Server-Tools/Windows-Server-Tools/MainWindow.xaml.cs
Windows-Server-Tools/Windows-Server-Tools/CommonlyInstalledWindowsComponents.xaml
Windows-Server-Tools/Windows-Server-Tools/CommonlyInstalledWindowsComponents.xaml.cs
Windows-Server-Tools/Windows-Server-Tools.Tests/Program.cs
Windows-Server-Tools/Windows-Server-Tools.Tests/Windows-Server-Tools.Tests.csproj
```

The solution file and primary WPF project file also include the focused test project and recovery source. Separate legacy Exchange/SCCM source files contain local hardening changes and require integration review as part of the same working copy.

## Integration checklist

- [ ] Review the complete working-copy diff and preserve unrelated changes.
- [ ] Commit the intended implementation, tests, and documentation with an exact revision.
- [ ] Reconcile with the current remote default branch without rewriting or dropping commits.
- [ ] Run the focused recovery build/executable on the integrated tree and retain the exact count.
- [ ] Repeat the fresh primary WPF compile on source copied from the integrated tree.
- [ ] Verify the remote default branch contains the intended revision.
- [ ] Record the exact revision, changed files, commands, counts, and remote workflow state in repository issue #1.
- [ ] Keep issue #1 open until the integration and required evidence are verified.
- [ ] Do not enable the legacy role launch controls without reviewed credential-safe artifacts and immutable pins.

## Operational notes

- Recovery state is under `%ProgramData%\Windows Server Tools`; logs are under `%LOCALAPPDATA%\Windows-Server-Tools\Logs`.
- An indeterminate operation is a decision point, not an ordinary Retry. Inspect the server before selecting either outcome.
- Corrupt-state repair resets application recovery knowledge only after archiving evidence; it does not establish the actual server state.
- A cleanup-only failure should never be replaced with a full workflow replay.
- The current local tests and scratch compile do not substitute for disposable Windows Server integration testing or a packaged installer test.

## Outstanding external evidence

- Exact integrated commit: pending.
- Remote default-branch proof: pending.
- Remote workflow result for the integrated revision: pending.
- Installer and artifact digest: not available for the current local changes.
- Release and deployment: not claimed.
