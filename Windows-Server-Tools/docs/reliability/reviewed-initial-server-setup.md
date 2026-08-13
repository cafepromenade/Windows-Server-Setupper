# Reviewed initial server setup

## Behavior

Opening the primary WPF application is read-only. A normal launch configures only local interface availability, handles an explicit command-line request when present, and displays pending recovery information. It does not change network settings, security controls, server roles, or installed software.

The **Review initial server setup plan…** action opens a native confirmation panel. The panel lists every operation in the batch before it can start:

- retain the active IPv4 address as a static address and configure its gateway and DNS server;
- turn off firewall profiles and Microsoft Defender real-time monitoring;
- remove AC standby, display, disk, and hibernation timeouts;
- enable Remote Desktop, disable Network Level Authentication and SmartScreen, and restart Explorer;
- install and configure DNS and DHCP server roles and authorize DHCP in Active Directory;
- change the secure-attention sequence setting;
- install the pinned Chocolatey 2.7.3 package.

Two independent confirmation keys must be set before the 0–100 authorization slider is enabled. Only a complete slider value enables **Authorize and run initial setup**. One confirmation instance can claim the batch once. Repeated activation is ignored.

## Cancellation and focus

**Emergency exit** and <kbd>Esc</kbd> close the panel without starting any operation. Cancelling permanently invalidates that confirmation instance, resets both keys and the slider, and returns focus to **Review initial server setup plan…**. The confirmation status is an assertive automation live region. Every key, slider, exit action, and authorization action has a programmatic name or help description.

## Recovery and ordering

The approved operations are submitted as one recovery batch. DNS and DHCP installation declares the static-network step as a dependency, so it cannot start unless that step succeeds. An indeterminate result creates a process-wide barrier inside the batch: no later operation runs until the uncertain action is explicitly reconciled. Confirmed failures may still permit an independent later action where that independence is declared and its outcome is definite.

Every WPF server mutation also holds `Coordination/server-mutation.lease` below the protected per-machine state root for the full operation lifetime. The lock uses exclusive file sharing, the same Administrators-and-Local-System access policy as recovery state, and zero-wait acquisition. A second WPF process cannot begin another server mutation while the first process owns the lease. Other runtimes must acquire this same lock contract before their own server mutations; until they do, cross-runtime exclusion remains a release blocker.

The canonical checkpoint remains authoritative. A valid primary checkpoint resumes even when a truncated crash-left temporary file exists; that invalid residue is moved outside the candidate filename patterns and cannot create a persistent corruption marker. A corrupt primary still fails closed even when another candidate parses successfully.

## Failure modes

| Failure | Result | Recovery |
| --- | --- | --- |
| One key or a partial slider | Authorization stays disabled; no operation starts. | Complete both keys and the full slider, or cancel. |
| Emergency exit or <kbd>Esc</kbd> | The confirmation instance is cancelled; no operation starts. | Open a new review panel if the plan should be reconsidered. |
| Static-network failure | DNS and DHCP remain blocked by their named dependency. | Correct the network condition and use the existing recovery action. |
| Indeterminate external process | Every later mutation in the batch remains blocked. | Inspect actual server state and use the explicit reconciliation choice. |
| Another WPF process owns the machine mutation lease | The requested action does not start or queue. | Let the owning operation stop, then retry from this window. |
| Invalid temporary residue with valid primary | The primary resumes; residue is quarantined outside candidate discovery. | No operator action is required. |
| Corrupt primary | Replay is blocked even if a temporary or backup candidate is valid. | Use the evidence-preserving corrupt-state repair flow after review. |

## Verification

The focused executable covers startup source contracts, both-key and full-slider boundaries, one-claim behavior, cancellation, dependency ordering, indeterminate barriers, valid-primary residue quarantine, corrupt-primary fail-closed behavior, and the existing recovery state machine. Build and packaged-runtime evidence must be recorded against the exact release candidate; this article does not claim a release or installation result.
