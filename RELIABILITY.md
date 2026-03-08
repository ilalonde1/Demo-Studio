# Reliability and Release Readiness

## Release Confidence Command
Run this before publishing or demoing:

```powershell
pwsh -NoLogo -NoProfile -File ./scripts/release-confidence-gate.ps1 -Configuration Release -SmokeSeconds 3
```

This performs:
- Desktop solution build
- Release-confidence test subset (`Gate=ReleaseConfidence`)
- Desktop smoke SLA run with metrics output

## What the Release Gate Verifies
- Golden-path compose/publish/recovery workflows.
- Deterministic failure modes (missing inputs and contract failures).
- Performance summary output contract (percentile reporting present).
- Artifact contracts (manifest fields and publish package contents).
- Cancellation safety for countdown/startup control paths.

## Reliability Signals to Track
- Build warnings for locked test binaries (`testhost` retries).
- Smoke metrics in `artifacts/release-confidence/smoke/metrics.json`.
- Runtime background failures reported via `DS-DESK-*` envelopes.

## Known Operational Constraints
- Desktop smoke and recorder flows are Windows-specific.
- External tooling availability (FFmpeg) still influences degraded/healthy runtime status.

## Reviewer Checklist
- CI green on PR (build + test + gate subset).
- Local release-confidence command passes.
- No regression in session lifecycle: start/pause/stop/compose/publish/recovery.
- No new silent exception swallowing in critical persistence or pipeline paths.
