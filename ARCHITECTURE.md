# DemoStudio Desktop Architecture

## Purpose
`DemoStudio.Desktop.App` is a production-style desktop recorder/composer/publisher workflow shell for demo creation, with reliability-first orchestration and explicit subsystem boundaries.

## System Structure
- `src/DemoStudio.Desktop.App`
  - WPF shell and workflow orchestration.
  - ViewModel composition root (`MainWindowViewModel`) with child ViewModels for focused state boundaries.
- `src/DemoStudio.Desktop.Core`
  - Session state machine (`RecorderSessionEngine`) and core recording state contracts.
- `src/DemoStudio.Application`
  - Application services/pipeline orchestration for domain workflows.
- `src/DemoStudio.Infrastructure`
  - Process, persistence, and external integration implementations.
- `src/DemoStudio.Domain`
  - Domain entities/value objects and invariants.
- `src/DemoStudio.Desktop.Smoke`
  - Runtime smoke and SLA probe executable.
- `tests/*`
  - Core and app-level reliability/behavior contracts.

## Desktop Shell Boundaries
`MainWindowViewModel` composes child state ViewModels to keep concerns isolated:
- `OnboardingViewModel`
- `TargetingLaunchViewModel`
- `SessionHistoryViewModel`
- `ProductionWorkspaceViewModel`
- `ClipCurationViewModel`
- `WorkflowStateViewModel`
- `SessionStateViewModel`

Command can-execute refresh is centralized via `CommandStateCoordinator`.

## Reliability Patterns
- In-flight guards (`Interlocked`) for timer-driven background operations.
- Lifecycle cancellation (`_lifecycleCancellation`) for long-running/step-delayed workflows.
- Background failure reporting with throttling (`ReportBackgroundFailureAsync`).
- Queue-based compose/publish execution (`DesktopFfmpegOperationQueue`) to limit concurrent FFmpeg pressure.

## Key Runtime Flows
- Capture start/pause/stop with preflight and watchdog protection.
- Clip curation and narration workflows.
- Compose pipeline to final output and health snapshot.
- Publish packaging with metadata/share artifacts.
- Draft recovery persistence for session continuation.

## Quality Gates
- Standard CI build + tests in `.github/workflows/ci.yml`.
- Release confidence subset tests tagged with `Trait("Gate","ReleaseConfidence")`.
- One-command local gate:
  - `pwsh -NoLogo -NoProfile -File ./scripts/release-confidence-gate.ps1 -Configuration Release -SmokeSeconds 3`
