# Resilient recovery and uncertain outcomes
Category: Reliability
Suggested: product-overview,releases-changelog-and-downloads,build-and-installer-route

## Durable state

Recovery uses the windows-server-tools-recovery-v3 format. Each operation is recorded before and after execution with a state, attempt count, generation, timestamps, bounded error summary, and an integrity record over canonical content.

## Retry rules

Automatic retry is reserved for operations explicitly declared idempotent and remains bounded by an attempt budget. A failed persistence write after an action starts or completes produces an uncertain result rather than a success claim.

## Reconciliation

An indeterminate action has two separate reviewed outcomes: it completed and should be preserved, or it was confirmed stopped without completing and may enter a new retry generation. One answer is never applied to several uncertain actions.

## Failure modes

Corrupt state blocks replay. External-process timeouts remain indeterminate unless the entire contained process tree is proven stopped. Cleanup failure is independently retryable and never silently repeats completed server work.
