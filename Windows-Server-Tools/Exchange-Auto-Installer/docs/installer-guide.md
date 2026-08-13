# Guided Exchange installation

The application separates review, preflight, plan approval, privileged execution, recovery, and postflight verification into durable stages. It never pre-fills credentials and never starts from a network, UNC, device, relative, linked, or redirected media path.

## Media choices

Administrators can select an existing local `Setup.exe` or use the repository Cheap LFS route. Cheap LFS metadata verification downloads zero ISO bytes and must verify all 13 release assets before the download action becomes available. The hydrated `exchange.iso` must be exactly 6,402,453,504 bytes with SHA-256 `cd2b13f2c297187776af4cff3541b4be3c677cf907cca69d85ab0e2b70377bd1`.

Local setup inspection requires the basename `Setup.exe`, a fixed local drive with no symbolic-link or junction component, a Microsoft Authenticode signature, Microsoft Exchange product metadata, the Exchange setup-media layout, and the same digest and file identity immediately before every execution.

## Indeterminate outcomes

A timeout, cancellation, missing process result, application interruption, or malformed completion probe is indeterminate. The application blocks resume and retry, records the evidence and a one-use token, and requires a reviewed outcome:

- **Confirmed completed** records the stage complete after the administrator checks external Exchange evidence.
- **Confirmed stopped without applying changes** records a conclusive stop and makes retry available.

A stale token cannot authorize the decision or start another privileged process.

## Restarts and retries

Exit codes 1641 and 3010 record a successful stage that requires a restart. The next stage does not run. The application requires a different operating-system boot marker and a fresh passing preflight before resume. Exit code 1618 is retried only within the configured bound; cancellation during backoff prevents another attempt.

Corrupt primary and backup state is preserved in a dated evidence directory. The application refuses to replace it with a fresh pending plan. A second process cannot mutate the same protected installation state.

## Verification

Run `npm run verify`, `npm run build`, `npm run package`, and `npm run verify:package`. The installer remains unsigned by permanent project policy. Windows may show an unknown-publisher or SmartScreen warning.
