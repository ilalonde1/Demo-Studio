# Safe Incremental Remediation Plan
**DemoStudio App Demo Maker — Phases 5–9 (Post-Phase-4 Audit)**
**Date**: 2026-03-09

> READ-ONLY PLANNING DOCUMENT. No code is modified here.
> Each step compiles independently. Each step is independently reversible.
> Phases 1–4 are complete. This document covers the remaining audit findings only.

---

## Completed Phases (summary)

| Phase | Title | Status |
|---|---|---|
| 1 | Stabilization (bare catches, scaffolding, crash reporter) | ✅ Done |
| 2 | DI Cleanup (constructor hardening, test builder, optional params) | ✅ Done |
| 3 | Config Centralization (DesktopRuntimePaths, DesktopStoragePaths) | ✅ Done |
| 4 | ViewModel Decomposition (HealthMonitor, PublishWorkflow, CaptureSession) | ✅ Done |

---

## Execution Rules

1. **Never batch phases.** Complete and verify each step before starting the next.
2. **Build gate**: `dotnet build DemoStudio.Desktop.sln -m:1 --no-incremental` passes clean.
3. **Test gate**: `dotnet test DemoStudio.Desktop.sln -m:1` passes all 34 tests.
4. **One commit per step.** Each step is independently revertable with `git revert`.
5. **Read each affected file fully** before starting any step — cross-file state access is the main risk.

---

## Phase 5 — Reliability Fixes

**Goal**: Eliminate the highest-severity runtime risks identified in the audit. All steps in this
phase are narrow, surgical, and carry zero behavior-change risk. Reliability fixes first,
architectural refactors later.

**Rationale**: Two findings are active defects (not just code quality issues): the
`Process.GetCurrentProcess()` dispose bug corrupts the process handle for the lifetime of the
application, and the dead DI registrations silently waste container memory and mislead future
readers. The `?? new DesktopProcessRunner()` fallbacks are a latent risk that becomes a real
defect the moment a test or alternate composition root tries to substitute a different runner.
All four steps in this phase are isolated, individually revertable, and independent of each other.

---

### Step 5.1 — Fix `Process.GetCurrentProcess()` disposal in `HealthMonitorViewModel`

**File affected**: `src/DemoStudio.Desktop.App/ViewModels/HealthMonitorViewModel.cs`

**Finding**: `CollectTelemetrySnapshot` (line ~216) uses `using var process = Process.GetCurrentProcess()`.
`Process.GetCurrentProcess()` returns a handle to the running process itself. Disposing it calls
`CloseHandle` on the pseudo-handle, setting the underlying handle to `IntPtr.Zero`. Any subsequent
call to `Process.Handle`, `Process.WorkingSet64`, or `Process.Refresh()` on the same process
object — or any new call to `Process.GetCurrentProcess()` — will fail or return stale data
for the remainder of the application lifetime. This is a documented .NET footgun.

**Exact change**: Remove the `using` keyword. Keep the `Refresh()` call.

```csharp
// Before:
using var process = Process.GetCurrentProcess();
process.Refresh();

// After:
var process = Process.GetCurrentProcess();
process.Refresh();
```

**Why it is safe**: `Process.GetCurrentProcess()` always returns a fresh view of the current
process. Not disposing it is correct here — the handle is owned by the runtime, not by this
method. `Refresh()` is still called to clear cached property values. Zero behavior change for
callers.

**Verification**:
```
dotnet build DemoStudio.Desktop.sln -m:1 --no-incremental
dotnet test DemoStudio.Desktop.sln -m:1
```

---

### Step 5.2 — Harden the four `?? new DesktopProcessRunner()` DI fallbacks

**Finding**: Four services accept an optional `DesktopProcessRunner` parameter and silently
self-instantiate a second runner if DI does not inject one. `DesktopProcessRunner` is registered
as a singleton in `DesktopCompositionRoot`. The fallback creates an invisible second instance with
no way to intercept, stub, or observe it. This is the same pattern corrected in Step 1.3 for
`DesktopStartupHealthService`.

**Files affected**:
```
src/DemoStudio.Desktop.App/Services/DesktopCaptureMediaCoordinator.cs
src/DemoStudio.Desktop.App/Services/DesktopDependencyHealthService.cs
src/DemoStudio.Desktop.App/Services/DesktopNarrationCoordinator.cs
src/DemoStudio.Desktop.App/Services/DesktopTargetLauncher.cs
```

**Pattern in each file** (exact line numbers will vary — read each file before editing):
```csharp
// Before (optional param with fallback):
public SomeService(DesktopCaptureRuntime captureRuntime, DesktopProcessRunner? processRunner = null)
{
    _processRunner = processRunner ?? new DesktopProcessRunner();
}

// After (required param with null guard):
public SomeService(DesktopCaptureRuntime captureRuntime, DesktopProcessRunner processRunner)
{
    _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
}
```

**Pre-change check for each file**: Grep the entire solution for `new DesktopXxxService(` and
`new DesktopXxxCoordinator(` to find any call site that omits `processRunner`. If a call site
exists outside `DesktopCompositionRoot`, update it first (or update `MainWindowViewModelTestBuilder`
if it is a test call site).

**Why it is safe**: `DesktopProcessRunner` is registered as a singleton. All four services are
registered in `DesktopCompositionRoot` using type-based resolution, so DI supplies the runner
automatically. `MainWindowViewModelTestBuilder.CreateMinimal()` already passes `processRunner`
explicitly to every coordinator it creates — confirm this covers all four services before committing.

**Do each service as a sub-step with its own build/test gate**:
- Step 5.2a — `DesktopCaptureMediaCoordinator`
- Step 5.2b — `DesktopDependencyHealthService`
- Step 5.2c — `DesktopNarrationCoordinator`
- Step 5.2d — `DesktopTargetLauncher`

**Verification after each sub-step**:
```
dotnet build DemoStudio.Desktop.sln -m:1 --no-incremental
dotnet test DemoStudio.Desktop.sln -m:1
```

---

### Step 5.3 — Remove the three dead DI singleton registrations

**Finding**: `DesktopCompositionRoot.cs` registers `HealthMonitorViewModel`,
`PublishWorkflowViewModel`, and `CaptureSessionViewModel` as singletons (lines 31–33). All three
are instantiated via `new` inside `MainWindowViewModel`'s constructor and never resolved from the
container. The registrations are dead code — they waste container memory and mislead future readers
into believing these ViewModels are container-managed.

**File affected**: `src/DemoStudio.Desktop.App/DesktopCompositionRoot.cs`

**Pre-change check**: Grep the entire solution for
`GetRequiredService<HealthMonitorViewModel>`,
`GetRequiredService<PublishWorkflowViewModel>`,
`GetRequiredService<CaptureSessionViewModel>`,
`GetService<HealthMonitorViewModel>`,
and `provider.GetRequiredService` variants. Confirm zero resolution call sites exist.

**Two valid resolutions** — choose one and note the choice in the commit message:

**Option A (remove)**: Delete the three `AddSingleton` lines. `MainWindowViewModel` continues to
construct them via `new` as today.

**Option B (resolve from DI)**: Keep the registrations. Change `MainWindowViewModel`'s constructor
to accept all three as injected parameters instead of instantiating them with `new`. This is the
cleaner long-term path and aligns with the Phase 4 decomposition intent, but requires updating
`MainWindowViewModelTestBuilder` as well.

**Recommendation**: Start with Option A (safer, zero risk). Option B can follow as a separate
Phase 5 step once Option A is verified.

**Option A — Exact change**: Remove these three lines from `DesktopCompositionRoot.cs`:
```csharp
services.AddSingleton<HealthMonitorViewModel>();       // line 31
services.AddSingleton<PublishWorkflowViewModel>();     // line 32
services.AddSingleton<CaptureSessionViewModel>();      // line 33
```

**Why it is safe**: Removing a registration that is never resolved has zero runtime impact.
No `GetRequiredService` call references these types, so removal cannot throw.

**Verification**:
```
dotnet build DemoStudio.Desktop.sln -m:1 --no-incremental
dotnet test DemoStudio.Desktop.sln -m:1
```

---

### Step 5.4 — Type the bare catch blocks in `DesktopVideoComposeService`

**Finding**: At least seven bare `catch { }` or `catch (Exception) { }` blocks in
`DesktopVideoComposeService` swallow exceptions silently with no log, no metric, and no error
propagation. In a compose pipeline this means a corrupt or stale output can be produced with no
observable signal.

**File affected**: `src/DemoStudio.Desktop.App/Services/DesktopVideoComposeService.cs`

**Approach**: Read the full file first. For each silent catch block:
1. If the failure is genuinely best-effort (e.g., cache pruning, temp directory cleanup), change
   `catch { }` to `catch (Exception)` and add a `// Best-effort: ...` comment explaining what
   is being swallowed and why it is safe to swallow.
2. If the failure is on a critical path (file existence check feeding into a compose decision),
   consider whether swallowing is correct — document the decision explicitly as a comment.

**Pattern for each block**:
```csharp
// Before:
catch { }

// After:
catch (Exception)
{
    // Best-effort [operation name]. If this fails the [consequence] is [safe fallback].
    // Intentionally not rethrown — callers observe [state/return value] instead.
}
```

**Do not add logging yet** (that is Phase 6 territory). This step only adds typing and comments.

**Why it is safe**: `catch (Exception)` catches everything a bare `catch { }` catches in managed
code. This is a zero-behavior-change documentation improvement.

**Verification**:
```
dotnet build DemoStudio.Desktop.sln -m:1 --no-incremental
dotnet test DemoStudio.Desktop.sln -m:1
```

---

## Phase 6 — DI Boundary Enforcement for Self-Instantiating Services

**Goal**: Eliminate the two services that construct their own collaborators inside method bodies,
making them untestable and impossible to substitute.

**Rationale**: `DesktopCaptureRuntime.BuildCaptureService()` and `DesktopSmokeCheckService.RunAsync()`
both call `new FfmpegVideoCaptureService(new ProcessLauncher(), new LocalFileStorage(), ...)` inside
method bodies. This makes it impossible to inject a stub or test double. The pattern mirrors the
DI fallback problem in Phase 2/5 but is more severe — there is no fallback path at all, the method
*always* self-instantiates. This phase enforces the DI boundary across the capture runtime stack.

---

### Step 6.1 — Inject a capture service factory into `DesktopCaptureRuntime`

**File affected**: `src/DemoStudio.Desktop.App/Services/DesktopCaptureRuntime.cs`

**Finding**: `BuildCaptureService()` (locate the method — it instantiates `ProcessLauncher`,
`LocalFileStorage`, `FfmpegVideoCaptureService` directly). This tightly couples the runtime to
a specific implementation and makes unit testing impossible.

**Approach**:
1. Read `DesktopCaptureRuntime.cs` fully before making any change.
2. Define a `Func<IVideoCaptureService>` factory delegate parameter in the constructor, or accept
   an `IVideoCaptureServiceFactory` if one already exists.
3. Replace the inline `new` chain in `BuildCaptureService()` with a call to the injected factory.
4. Register the factory lambda in `DesktopCompositionRoot` to keep production behavior identical.
5. Update `MainWindowViewModelTestBuilder` to supply a no-op factory.

**Pre-step check**: Read `DesktopCaptureRuntime.cs` and the capture abstractions project to
determine whether `IVideoCaptureServiceFactory` already exists. If it does, use it. If not,
a `Func<IVideoCaptureService>` delegate is sufficient for this step.

**Verification**:
```
dotnet build DemoStudio.Desktop.sln -m:1 --no-incremental
dotnet test DemoStudio.Desktop.sln -m:1
```

---

### Step 6.2 — Inject capture service dependencies into `DesktopSmokeCheckService`

**File affected**: `src/DemoStudio.Desktop.App/Services/DesktopSmokeCheckService.cs`

**Finding**: `RunAsync()` calls `new FfmpegVideoCaptureService(new ProcessLauncher(), new LocalFileStorage(), new NullWindowLocator())` inside the method body. Because `RunAsync` is the only public method, this makes the entire service untestable — any test would run real FFmpeg.

**Approach**:
1. Read `DesktopSmokeCheckService.cs` fully.
2. Accept the same `Func<IVideoCaptureService>` factory (or the concrete service directly if
   a factory is too heavy) as a constructor parameter.
3. Replace the inline instantiation with the injected dependency.
4. Register via the factory lambda in `DesktopCompositionRoot`.
5. Update `MainWindowViewModelTestBuilder` to supply a stub.

**Why this step must follow Step 6.1**: Both services need the same factory contract. Agree on the
factory pattern in Step 6.1 before applying it to the smoke check service.

**Verification**:
```
dotnet build DemoStudio.Desktop.sln -m:1 --no-incremental
dotnet test DemoStudio.Desktop.sln -m:1
```

---

## Phase 7 — Path Construction Centralization

**Goal**: Eliminate the duplicated `Path.Combine(StorageRoot, "curated")` and related sub-path
formulas scattered across at least five files. Establish `DesktopStoragePaths` as the single
canonical source for all well-known sub-paths under the storage root.

**Rationale**: `DesktopStoragePaths` was created in Phase 3 to centralize the root path formula
(`%LOCALAPPDATA%\DemoStudio\RecorderDesktop`). The audit found the sub-path formulas — `curated`,
`narration`, `previews`, `thumbnails`, `compose-cache`, `compose-telemetry.jsonl`, `publish` — are
still duplicated inline. Each duplication is a divergence risk: if the folder layout changes, every
inline occurrence must be found and updated.

---

### Step 7.1 — Extend `DesktopStoragePaths` with sub-path helpers

**File affected**: `src/DemoStudio.Desktop.App/Infrastructure/DesktopStoragePaths.cs`

**Exact additions** (do not remove any existing members):

```csharp
/// <summary>Returns the curated output directory for a given raw video file path.</summary>
public static string GetCuratedDirectory(string rawVideoPath) =>
    Path.Combine(Path.GetDirectoryName(rawVideoPath)
        ?? throw new ArgumentException("Path has no directory.", nameof(rawVideoPath)),
        "curated");

/// <summary>Returns the narration directory for a session under the storage root.</summary>
public static string GetNarrationDirectory(string storageRoot, Guid sessionId) =>
    Path.Combine(storageRoot, "narration", sessionId.ToString("N"));

/// <summary>Returns the previews directory for a session key under the storage root.</summary>
public static string GetPreviewsDirectory(string storageRoot, string sessionKey) =>
    Path.Combine(storageRoot, "previews", sessionKey);

/// <summary>Returns the thumbnails directory for a session key under the storage root.</summary>
public static string GetThumbnailsDirectory(string storageRoot, string sessionKey) =>
    Path.Combine(storageRoot, "thumbnails", sessionKey);

/// <summary>Returns the compose cache directory for a curated directory.</summary>
public static string GetComposeCacheDirectory(string curatedDirectory) =>
    Path.Combine(curatedDirectory, "compose-cache");

/// <summary>Returns the compose telemetry log path for a curated directory.</summary>
public static string GetComposeTelemetryPath(string curatedDirectory) =>
    Path.Combine(curatedDirectory, "compose-telemetry.jsonl");

/// <summary>Returns the compose health snapshot path for a curated directory.</summary>
public static string GetComposeHealthPath(string curatedDirectory) =>
    Path.Combine(curatedDirectory, "compose-health.json");

/// <summary>Returns the publish output directory under the storage root.</summary>
public static string GetPublishDirectory(string storageRoot) =>
    Path.Combine(storageRoot, "publish");
```

**Why it is safe**: Additive only. No existing code is touched. All helpers are pure functions.
Zero behavioral change.

**Verification**:
```
dotnet build DemoStudio.Desktop.sln -m:1 --no-incremental
dotnet test DemoStudio.Desktop.sln -m:1
```

---

### Step 7.2 — Update `DesktopVideoComposeService` to use centralized path helpers

**File affected**: `src/DemoStudio.Desktop.App/Services/DesktopVideoComposeService.cs`

**Pre-step**: Read the file fully and map every inline `Path.Combine` call. Replace each one with
the corresponding `DesktopStoragePaths` helper from Step 7.1. Do not rename local variables or
change any logic — only replace path-building expressions.

**Do each replaced formula as a reviewable comment in the diff** so reviewers can confirm the
output path is identical:
```csharp
// Before: Path.Combine(rawDirectory, "curated")
// After:  DesktopStoragePaths.GetCuratedDirectory(rawVideoPath)
```

**Verification**:
```
dotnet build DemoStudio.Desktop.sln -m:1 --no-incremental
dotnet test DemoStudio.Desktop.sln -m:1
```

---

### Step 7.3 — Update remaining consumers to use centralized path helpers

**Files affected** (read each before editing):
```
src/DemoStudio.Desktop.App/Services/DesktopNarrationCoordinator.cs
src/DemoStudio.Desktop.App/Services/DesktopCaptureMediaCoordinator.cs
src/DemoStudio.Desktop.App/ViewModels/PublishWorkflowViewModel.cs
```

**Same approach as Step 7.2**: find every inline `Path.Combine` that matches a known sub-path
pattern and replace with the helper. One file at a time, one build/test gate per file.

**Sub-steps**:
- Step 7.3a — `DesktopNarrationCoordinator` (narration sub-paths)
- Step 7.3b — `DesktopCaptureMediaCoordinator` (previews, thumbnails sub-paths)
- Step 7.3c — `PublishWorkflowViewModel` (publish, compose-health sub-paths)

**Verification after each sub-step**:
```
dotnet build DemoStudio.Desktop.sln -m:1 --no-incremental
dotnet test DemoStudio.Desktop.sln -m:1
```

---

## Phase 8 — `DesktopVideoComposeService` Decomposition

**Goal**: Break the 993-line, five-responsibility `DesktopVideoComposeService` into focused,
independently testable components.

**Rationale**: The service currently owns: (1) FFmpeg orchestration, (2) compose-cache management
and pruning, (3) file-signature hashing for cache invalidation, (4) telemetry JSONL writing, and
(5) compose-health snapshot writing. None of these are testable in isolation because they are all
embedded in one class. After Phase 7 centralizes the paths, the responsibility boundaries become
clean enough to extract.

**Blocked until**: Phase 7 is fully complete and all tests are green. Path centralization is a
prerequisite — without it, the extracted classes would immediately re-introduce the duplication.

---

### Step 8.1 — Extract `DesktopComposeTelemetryWriter`

**New file**: `src/DemoStudio.Desktop.App/Services/DesktopComposeTelemetryWriter.cs`

**Responsibility**: Append telemetry entries to the `compose-telemetry.jsonl` file in a curated
directory. This is pure I/O with no FFmpeg dependency.

**Approach**:
1. Read `DesktopVideoComposeService.cs` fully. Identify all methods that write to
   `compose-telemetry.jsonl`.
2. Move those methods into `DesktopComposeTelemetryWriter` verbatim — no logic changes.
3. In `DesktopVideoComposeService`, inject `DesktopComposeTelemetryWriter` via constructor and
   replace the moved method calls with calls to the injected instance.
4. Register `DesktopComposeTelemetryWriter` in `DesktopCompositionRoot`.
5. Update `MainWindowViewModelTestBuilder` if it directly constructs `DesktopVideoComposeService`.

**Verification**:
```
dotnet build DemoStudio.Desktop.sln -m:1 --no-incremental
dotnet test DemoStudio.Desktop.sln -m:1
```

---

### Step 8.2 — Extract `DesktopComposeCacheService`

**New file**: `src/DemoStudio.Desktop.App/Services/DesktopComposeCacheService.cs`

**Responsibility**: Cache validity checks, file-signature hashing, cache directory pruning. This
has no FFmpeg dependency — it is pure filesystem work.

**Approach**:
1. Identify all cache-related methods in `DesktopVideoComposeService`: `IsUsableFile`,
   `BuildFileSignature`, `TryPruneComposeCache`, `TryDeleteDirectory`, and any cache-read/write
   methods.
2. Move them verbatim into `DesktopComposeCacheService`.
3. Inject `DesktopComposeCacheService` into `DesktopVideoComposeService` and replace all call sites.
4. Register in `DesktopCompositionRoot`.

**Why this order**: Step 8.1 first because telemetry writing is the smallest, purest extraction.
Step 8.2 second because cache management is the next cleanest boundary. Each step reduces
`DesktopVideoComposeService` by ~150–200 lines, making the next step safer.

**Verification**:
```
dotnet build DemoStudio.Desktop.sln -m:1 --no-incremental
dotnet test DemoStudio.Desktop.sln -m:1
```

---

### Step 8.3 — Slim `DesktopVideoComposeService` to pure FFmpeg orchestration

**Goal**: After Steps 8.1 and 8.2, `DesktopVideoComposeService` should contain only:
- Manifest reading (or delegate to `DesktopComposeManifestService`)
- FFmpeg argument construction
- FFmpeg process execution
- Compose-health snapshot writing

**Approach**:
1. Read the remaining body of `DesktopVideoComposeService` after Steps 8.1–8.2.
2. If compose-health writing is still inline, extract it to a short private helper or inline it
   into a well-named method so the orchestration flow is readable in one screen.
3. Verify the class is under 350 lines after extraction.

**No new extractions in this step** — this is a cleanup and verification pass, not another
extraction. If the class is still oversized, plan an additional extraction step before proceeding.

**Verification**:
```
dotnet build DemoStudio.Desktop.sln -m:1 --no-incremental
dotnet test DemoStudio.Desktop.sln -m:1
```

---

## Phase 9 — Test Coverage Baseline

**Goal**: Establish unit test coverage on the most critical logic paths currently at zero coverage.
Focus on pure logic that can be tested without real FFmpeg, real files, or WPF context.

**Rationale**: The audit found zero test coverage on all major services. This phase does not aim
for comprehensive coverage — it targets the logic that (a) has no infrastructure dependency, (b)
protects high-risk code paths, and (c) gives the fastest return on investment. Integration tests
against real infrastructure are out of scope for this phase.

**Blocked until**: Phase 8 is complete. Extraction makes the services individually testable.
Tests written before extraction would immediately become stale.

---

### Step 9.1 — Unit tests for `DesktopComposeCacheService` (file signature and pruning logic)

**New file**: `tests/DemoStudio.Desktop.App.Tests/Services/DesktopComposeCacheServiceTests.cs`

**What to test**:
- `BuildFileSignature` returns consistent output for the same inputs
- `BuildFileSignature` returns different output when inputs differ
- `IsUsableFile` returns false for non-existent paths
- `TryPruneComposeCache` does not throw on an empty directory
- `TryDeleteDirectory` handles a missing directory gracefully

**Setup**: Use `Path.GetTempPath()` + `Guid` for temporary directories. Delete in `[TearDown]`.

**Verification**:
```
dotnet test DemoStudio.Desktop.sln -m:1 --filter "DesktopComposeCacheService"
dotnet test DemoStudio.Desktop.sln -m:1   # full suite still green
```

---

### Step 9.2 — Unit tests for `HealthMonitorViewModel` budget and parse logic

**New file**: `tests/DemoStudio.Desktop.App.Tests/ViewModels/HealthMonitorViewModelTests.cs`

**What to test** (pure logic — no I/O, no WPF):
- `TryParseWriteRate`: returns false for `"-"`, `"warming up"`, empty string; parses valid rates
- `TryParseComposeDurationSeconds`: returns false for `"-"`, invalid format; parses valid durations
- `UpdatePerformanceBudgetState`: correct label/color for Healthy / Watch / Critical thresholds
  - Working set below 800 MB → Healthy
  - Working set 800–1099 MB → Watch
  - Working set ≥ 1100 MB → Critical
  - Write rate > 60 MB/s → Critical
  - Compose runtime > 360s → Critical

**Setup**: Construct `HealthMonitorViewModel` with stub implementations of its four dependencies.
Create stubs inline in the test file (no separate files needed for this step).

**Verification**:
```
dotnet test DemoStudio.Desktop.sln -m:1 --filter "HealthMonitorViewModel"
dotnet test DemoStudio.Desktop.sln -m:1
```

---

### Step 9.3 — Unit tests for `DesktopStoragePaths` path helpers

**New file**: `tests/DemoStudio.Desktop.App.Tests/Infrastructure/DesktopStoragePathsTests.cs`

**What to test**:
- `GetDefaultRecorderRoot` returns a path ending in `DemoStudio\RecorderDesktop`
- `GetCuratedDirectory` appends `\curated` to the directory of the input path
- `GetNarrationDirectory` formats the session ID as `N` (no hyphens)
- `GetPublishDirectory` appends `\publish` to the storage root
- All helpers use `Path.Combine` (not string concatenation) — test with paths containing spaces

**Why this matters**: These helpers will be used by multiple services after Phase 7. Testing them
now protects against any path-separator or encoding regression introduced by the centralization.

**Verification**:
```
dotnet test DemoStudio.Desktop.sln -m:1 --filter "DesktopStoragePaths"
dotnet test DemoStudio.Desktop.sln -m:1
```

---

### Step 9.4 — Unit tests for `RecorderSessionEngine` state machine

**New file**: `tests/DemoStudio.Desktop.App.Tests/Services/RecorderSessionEngineTests.cs`

**What to test** (pure state transitions — no I/O):
- Initial state is `Armed`
- `StartOrResumeClip` from `Armed` transitions to `Recording`, increments clip count
- `PauseClip` from `Recording` transitions to `Paused`
- `StartOrResumeClip` from `Paused` transitions to `Recording`, adds a new clip
- `StopCompleted` from `Recording` transitions to `Armed`, session ID is preserved
- `StopFailed` from `Recording` transitions to `Failed`, failure reason is stored
- `Reset` from `Failed` transitions to `Armed`, clears clips
- `Snapshot()` is immutable — mutating engine state does not mutate a previously taken snapshot

**Why this matters**: `RecorderSessionEngine` is the central state machine for the entire capture
lifecycle. It currently has zero tests. A regression here could silently break the entire recording
flow without a compile error.

**Verification**:
```
dotnet test DemoStudio.Desktop.sln -m:1 --filter "RecorderSessionEngine"
dotnet test DemoStudio.Desktop.sln -m:1
```

---

## Summary Table

| Step | Phase | File(s) | Risk | Effort |
|---|---|---|---|---|
| 5.1 | Reliability | HealthMonitorViewModel.cs | Zero | 5 min |
| 5.2a–d | Reliability | 4 × service constructors | Very Low | 10 min each |
| 5.3 | Reliability | DesktopCompositionRoot.cs | Zero | 5 min |
| 5.4 | Reliability | DesktopVideoComposeService.cs | Zero | 20 min |
| 6.1 | DI Boundary | DesktopCaptureRuntime.cs | Medium | 45 min |
| 6.2 | DI Boundary | DesktopSmokeCheckService.cs | Low | 30 min |
| 7.1 | Paths | DesktopStoragePaths.cs | Zero | 20 min |
| 7.2 | Paths | DesktopVideoComposeService.cs | Very Low | 30 min |
| 7.3a–c | Paths | 3 × services/viewmodels | Very Low | 15 min each |
| 8.1 | Decomposition | New DesktopComposeTelemetryWriter | Medium | 1–2 hr |
| 8.2 | Decomposition | New DesktopComposeCacheService | Medium | 2–3 hr |
| 8.3 | Decomposition | DesktopVideoComposeService slim | Low | 30 min |
| 9.1 | Tests | New cache service tests | Low | 45 min |
| 9.2 | Tests | New HealthMonitorViewModel tests | Low | 1 hr |
| 9.3 | Tests | New DesktopStoragePaths tests | Low | 30 min |
| 9.4 | Tests | New RecorderSessionEngine tests | Low | 1 hr |
