# DemoStudio Architecture Remediation Plan

## 1. Stabilization Phase (Critical Issues)

This phase is intended to reduce data-loss, shutdown, and unsafe IO risks without changing the product's major runtime behavior. These tasks should be completed before larger architectural refactors.

### 1.1 LocalFileStorage filesystem sandboxing

#### Problem
`LocalFileStorage` currently accepts rooted paths and weakly normalizes relative paths. Callers can read or write outside the intended storage root.

#### Architectural Risk
- Breaks the storage boundary.
- Creates path traversal and arbitrary file access risk.
- Makes higher-level services unable to rely on storage contracts.

#### Remediation Strategy
Convert `LocalFileStorage` into an enforced sandboxed storage adapter with explicit escape hatches only where truly required and deliberately modeled.

#### Implementation Tasks
- Define the storage contract for runtime-owned files versus externally supplied absolute paths.
- Introduce canonical path resolution and base-root enforcement.
- Reject rooted paths unless the interface explicitly supports trusted absolute access.
- Reject relative paths that escape the base root after normalization.
- Add a separate abstraction for trusted external file access if required by automation/export flows.
- Review all `IFileStorage` call sites and classify them as:
  - runtime-owned storage
  - trusted external input
  - invalid usage to eliminate
- Document allowed storage roots for desktop runtime, smoke runs, compose cache, diagnostics, and export artifacts.

#### Verification Steps
- Add tests proving path traversal attempts are rejected.
- Add tests proving rooted paths outside the sandbox are rejected or routed through a separate trusted abstraction.
- Regression test capture, export, FlaUI runner payloads, and smoke flows against the new storage contract.

### 1.2 Shutdown and disposal lifecycle correction

#### Problem
Desktop shutdown currently mixes async event cleanup, fire-and-forget disposal, and container disposal during app exit.

#### Architectural Risk
- Background cleanup can be interrupted mid-flight.
- Services may be disposed while still executing.
- Session persistence, diagnostics, and process shutdown can become nondeterministic.

#### Remediation Strategy
Define a single shutdown coordinator and an explicit app lifecycle contract so window close, background operations, and container disposal occur in a deterministic order.

#### Implementation Tasks
- Define a runtime shutdown sequence for:
  - UI close request
  - cancellation of background work
  - capture/process stop
  - persistence flush
  - view model disposal
  - service provider disposal
- Remove fire-and-forget disposal from UI disposal entry points.
- Ensure app exit waits for shutdown completion or transitions to a controlled fail-fast path with diagnostics.
- Identify all long-running background operations and register them with lifecycle cancellation.
- Add a runtime state marker indicating shutdown-in-progress so timers and commands stop scheduling new work.
- Review watchdog, autosave, telemetry refresh, stage workspace, and process orchestration paths for shutdown participation.

#### Verification Steps
- Add lifecycle tests covering close during capture, close during compose, and close during autosave.
- Verify no ObjectDisposedException is emitted during normal shutdown.
- Verify diagnostics and session history still persist during orderly shutdown.

### 1.3 Atomic persistence for session files

#### Problem
Session history, draft recovery, and launch profile JSON files are written in place and corrupt files are silently treated as empty or missing.

#### Architectural Risk
- Operator history and recovery data can be lost on partial writes.
- Corruption is hidden rather than surfaced.
- Recovery guarantees are weaker than the UI implies.

#### Remediation Strategy
Adopt an atomic file persistence pattern with corruption detection, backup retention, and explicit degraded-state reporting.

#### Implementation Tasks
- Define a shared JSON persistence utility for desktop session files.
- Implement temp-file write plus atomic replace semantics.
- Keep the previous good file as a backup where supported.
- Add corruption markers or checksum/version metadata to persisted documents.
- Surface corruption detection to runtime diagnostics instead of silently returning empty data.
- Apply the persistence utility to:
  - session history
  - session draft recovery
  - launch profiles
  - demo templates
  - other operator-managed JSON state

#### Verification Steps
- Add tests for interrupted writes and malformed JSON recovery.
- Verify last-known-good backup can be restored.
- Verify the desktop runtime enters a visible degraded state when persistence corruption is detected.

## 2. Runtime Architecture Phase

This phase resolves the main layering problems while preserving feature behavior. It should begin only after the stabilization phase is complete.

### Goals
- Separate UI concerns from workflow orchestration.
- Move production workflow logic into `Application` services.
- Reduce direct coupling from `Desktop.App` to `Infrastructure`.
- Define a clean application boundary for desktop runtime use cases.

### Tasks
- Define desktop runtime use cases in `Application`, such as:
  - initialize desktop runtime
  - start capture session
  - stop/finalize session
  - run preflight
  - compose output
  - publish package
  - run smoke check
  - restore draft session
- Move workflow orchestration out of `MainWindowViewModel` into application-layer coordinators/use cases.
- Keep `MainWindowViewModel` responsible for:
  - view state
  - command wiring
  - UI presentation mapping
  - dispatching user intent to application services
- Replace direct infrastructure behavior in the UI layer with application abstractions.
- Define DTOs/results for desktop workflows so the UI does not consume infrastructure details directly.
- Reduce `Desktop.App` project references until it depends on:
  - `Application`
  - `Desktop.Core` if still needed for UI-only state contracts
  - UI-specific abstractions only
- Introduce explicit boundaries for:
  - capture subsystem
  - automation subsystem
  - publishing/composition subsystem
  - persistence/session-state subsystem

### Prerequisites
- Stabilization Phase complete.
- Initial typed configuration and composition root work started.

### High-Risk Refactors
- Moving capture/session workflow logic out of `MainWindowViewModel`.
- Replacing direct service-to-service calls with application use cases.
- Removing direct infrastructure project dependencies from `Desktop.App`.

### Verification
- Preserve current feature workflows through regression testing:
  - startup
  - target launch
  - recording lifecycle
  - watchdog stop
  - clip curation
  - compose
  - publish
  - session recovery

## 3. Dependency Injection and Composition Root

This phase establishes a single runtime composition model for the desktop product.

### Goals
- Centralize service registration.
- Eliminate ad hoc construction in runtime services.
- Register logging consistently.
- Bind and validate typed configuration at startup.

### Tasks
- Define a single runtime composition root for the desktop app.
- Register all runtime services in one place, including application services, infrastructure services, automation engines, capture services, persistence, and logging.
- Replace direct `new` construction inside runtime services with constructor injection.
- Remove direct child view model construction where those components require runtime services.
- Register `ILogger` support for desktop runtime and ensure infrastructure/application services receive loggers from DI.
- Bind typed configuration objects from configuration sources rather than manual JSON parsing.
- Wire all existing validators into startup validation.
- Fail startup with actionable diagnostics when required configuration is invalid.
- Decide whether the desktop runtime should use:
  - pure in-process composition
  - hosted service model
  - hybrid desktop composition with hosted backend services
- Standardize service lifetime rules and document them.

### Prerequisites
- Runtime application boundary defined.

### High-Risk Refactors
- Replacing manual construction inside capture/runtime services.
- Rewiring application and infrastructure services into one runtime graph.

### Verification
- Add a composition-root test that resolves the full runtime graph.
- Add startup validation tests for missing/invalid configuration.
- Verify all runtime services can be created without fallback constructors.

## 4. Process Execution Safety

This phase standardizes every external process boundary.

### Goals
- Remove raw string argument construction where practical.
- Route process execution through one policy.
- Standardize timeout, cancellation, disposal, and diagnostics handling.

### Tasks
- Replace raw command string assembly with `ProcessStartInfo.ArgumentList` or equivalent typed argument builders.
- Define one process execution policy in `IProcessLauncher`.
- Route FFmpeg, automation runner, smoke runner, thumbnail generation, and diagnostics probes through `IProcessLauncher`.
- Minimize direct `Process.Start` usage in desktop services.
- Standardize:
  - timeout defaults
  - cancellation behavior
  - graceful shutdown behavior
  - kill-tree behavior
  - stdout/stderr capture rules
- Review shell-executed launches and split them into:
  - trusted shell integrations such as Explorer
  - untrusted executable launches that must not use shell execution
- Add structured process execution diagnostics with operation IDs and session correlation.

### Prerequisites
- Composition root work underway so `IProcessLauncher` is consistently injected.

### High-Risk Refactors
- FFmpeg invocation changes across capture, compose, export, narration, and publish.
- Target launch behavior changes where `UseShellExecute` is currently relied upon.

### Verification
- Add tests for process timeout, cancellation, and stderr capture behavior.
- Run regression tests for capture, compose, publish, smoke, and FlaUI runner execution.

## 5. Persistence Safety

This phase broadens persistence work beyond the stabilization hotfixes.

### Goals
- Make desktop state persistence resilient and diagnosable.
- Prevent silent data loss.
- Provide operator recovery paths.

### Tasks
- Implement a shared atomic file write pattern for all desktop JSON state.
- Add corruption detection and schema/version markers to persisted documents.
- Define backup retention rules for session state files.
- Add restore behavior for:
  - latest good draft
  - latest good session history
  - latest good launch profiles
- Emit runtime diagnostics whenever backup restore or corruption recovery occurs.
- Define a cleanup policy for stale backups and temp files.
- Review compose cache, diagnostics, and publish artifacts for bounded growth and cleanup reporting.

### Prerequisites
- Stabilization persistence work complete.

### Verification
- Corrupt-file recovery tests.
- Backup restore tests.
- Disk cleanup and retention tests.

## 6. Logging and Observability

This phase unifies runtime diagnostics across desktop, application, and infrastructure layers.

### Goals
- Integrate `ILogger` across the desktop runtime.
- Ensure infrastructure and application services receive logging.
- Remove silent operational failures.
- Correlate logs with capture sessions and process execution.

### Tasks
- Introduce a logging provider configuration for the desktop runtime.
- Bridge or replace the custom runtime log service so it participates in the common logging model.
- Ensure all application and infrastructure services receive `ILogger<T>` from DI.
- Remove silent catch blocks where state, persistence, process execution, or recovery can be affected.
- Replace swallowed exceptions with:
  - log and continue
  - log and degrade
  - log and fail-fast
  based on operational importance
- Add correlation identifiers for:
  - session ID
  - compose/publish operation ID
  - process launch ID
  - diagnostics bundle ID
- Ensure background task failures are logged consistently and attached to session context.

### Prerequisites
- Composition root in place.

### Verification
- Confirm infrastructure/application logs are emitted during desktop execution.
- Verify startup, capture, compose, publish, and shutdown logs share correlated IDs.
- Review log output for silent-loss scenarios discovered in the audit.

## 7. Codebase Simplification

This phase reduces long-term maintenance cost and shrinks the highest-risk classes.

### Goals
- Break apart oversized orchestration classes.
- Clarify ownership boundaries.
- Decide which existing application-layer code is strategic versus redundant.

### Tasks
- Split `MainWindowViewModel` into smaller UI-focused components organized by workflow area.
- Extract command orchestration from view state management.
- Split `DesktopVideoComposeService` into separate responsibilities such as:
  - compose plan building
  - FFmpeg stage execution
  - cache management
  - compose telemetry/health reporting
  - style/quality profile selection
- Review the existing `Application` services and decide for each whether to:
  - integrate into production runtime
  - replace with new desktop-focused application services
  - remove as unused/dead code
- Review stub implementations and determine whether they remain test-only, development-only, or removable.
- Remove package and project references that are not part of the target runtime architecture.

### Prerequisites
- Runtime architecture and DI phases started.

### High-Risk Refactors
- Splitting `MainWindowViewModel`.
- Splitting `DesktopVideoComposeService`.
- Removing or re-homing unused application/infrastructure services.

### Verification
- Maintain feature parity through focused regression suites.
- Track file/class size reduction and dependency reduction as explicit success metrics.

## 8. Security Hardening

This phase formalizes the storage and execution hardening work.

### Goals
- Enforce filesystem boundaries.
- Validate executable launch inputs.
- Make runtime executable discovery deterministic and safe.

### Tasks
- Enforce filesystem sandboxing in storage abstractions.
- Validate launch profile executable paths against trusted rules.
- Add validation for launch arguments, or move to structured launch definitions where feasible.
- Separate trusted operator-selected executables from auto-discovered runtime executables.
- Replace broad runtime executable discovery with deterministic configuration or known deployment layout resolution.
- Review shell-based integrations and limit them to explicit user-facing operations.
- Add security-oriented diagnostics for rejected paths, invalid launch profiles, and unsafe executable discovery.

### Prerequisites
- Stabilization filesystem work complete.
- Process execution standardization underway.

### Verification
- Security regression tests for path traversal, invalid launch paths, and runner resolution.
- Manual validation that trusted launch flows still work for intended operator scenarios.

## 9. Architectural Target State

### Desired Layering

#### UI (`Desktop.App`)
- WPF windows, view models, command bindings, presentation state, dispatcher interactions.
- No direct infrastructure orchestration.
- Depends on `Application` contracts and UI-specific abstractions only.

#### Application Services
- Owns recording, preflight, compose, publish, smoke, recovery, and diagnostics workflows.
- Defines use cases, result types, orchestration policies, and service contracts.
- Coordinates domain rules and infrastructure capabilities.

#### Domain
- Owns core entities, invariants, value objects, and session/business rules that are independent of delivery mechanisms.

#### Infrastructure
- Implements persistence, process execution, capture engines, automation engines, filesystem adapters, logging providers, and external tool integration.
- Depends inward on `Application` and `Domain`, never on UI.

### Desktop Automation and Capture Placement
- Desktop automation engines and inspection runners are infrastructure adapters behind application abstractions.
- Capture engines are infrastructure services behind capture interfaces.
- Desktop-specific workflow policies remain in `Application`, not in WPF view models.
- If `Desktop.Core` remains, it should contain narrowly scoped UI-agnostic session state logic and not become a second orchestration layer.

## 10. Remediation Roadmap

1. Phase 1: Runtime Stabilization
   - Expected scope: storage sandboxing, atomic session persistence, shutdown sequencing, corruption visibility.
   - Risk level: Medium
   - Approximate effort: 2-3 weeks

2. Phase 2: Composition Root and Configuration Foundation
   - Expected scope: single runtime composition root, typed config binding, validator integration, logging registration, removal of critical manual construction.
   - Risk level: Medium
   - Approximate effort: 2-4 weeks

3. Phase 3: Process Execution Standardization
   - Expected scope: central process execution policy, FFmpeg/runner launch standardization, timeout and cancellation consistency.
   - Risk level: Medium-High
   - Approximate effort: 2-3 weeks

4. Phase 4: Runtime Architecture Extraction
   - Expected scope: move workflow orchestration from `MainWindowViewModel` into `Application` services, define desktop use cases, reduce UI coupling.
   - Risk level: High
   - Approximate effort: 4-6 weeks

5. Phase 5: UI Boundary Cleanup
   - Expected scope: remove direct `Infrastructure` references from `Desktop.App`, align project references with target architecture, simplify view models.
   - Risk level: High
   - Approximate effort: 3-5 weeks

6. Phase 6: Persistence and Observability Hardening
   - Expected scope: backup/restore strategy, corruption diagnostics, correlated logging, silent-catch removal in operational paths.
   - Risk level: Medium
   - Approximate effort: 2-3 weeks

7. Phase 7: Service Decomposition and Codebase Simplification
   - Expected scope: split `MainWindowViewModel`, split `DesktopVideoComposeService`, evaluate dead or duplicate application services, remove redundant code.
   - Risk level: High
   - Approximate effort: 4-6 weeks

8. Phase 8: Security Hardening and Deployment Determinism
   - Expected scope: launch validation, safer executable discovery, final filesystem policy enforcement, deployment/runtime assumption cleanup.
   - Risk level: Medium
   - Approximate effort: 2-3 weeks

### Recommended Ordering Notes
- Do not begin the major UI/application separation before stabilization and composition root work are in place.
- Treat Phase 4 and Phase 5 as the main architectural migration window.
- Treat Phase 7 as follow-through cleanup after the new architecture is proven stable.
