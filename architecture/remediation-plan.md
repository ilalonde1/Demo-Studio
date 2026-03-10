# Safe Incremental Remediation Plan
**DemoStudio App Demo Maker — Based on Forensic Audit**
**Date**: 2026-03-09

> READ-ONLY PLANNING DOCUMENT. No code is modified here.
> Each step compiles independently. Each step is independently reversible.
> Corrections from live code read-back are noted where the audit was inaccurate.

---

## Pre-Plan Corrections (from reading actual source)

Two audit findings were **inaccurate** and are removed from this plan:

| Audit Finding | Actual State | Action |
|---|---|---|
| "Missing index on `DemoRun.Status`" | `DemoRunConfiguration.cs:43–44` already has `HasIndex(x => x.Status)` and `HasIndex(x => x.QueuedAtUtc)` | **Removed from plan** |
| "Single-arg constructor is dead code" | `ReleaseConfidenceGateTests.cs:209` calls `new MainWindowViewModel(new RecorderSessionEngine(...))` | **Requires test migration before constructor removal** |

---

## Execution Rules

1. **Never batch phases.** Complete and verify each step before starting the next.
2. **Build gate**: Every step ends with `dotnet build` passing clean before committing.
3. **Test gate**: Every step ends with `dotnet test` passing clean before committing.
4. **One commit per step.** This makes each step independently revertable with `git revert`.
5. **Phase 4 steps are blocked until Phase 2 is complete.** ViewModel decomposition is only safe once the constructor is clean.
6. **Read each affected partial class fully** before starting any Phase 4 step — cross-partial state access is the main decomposition risk.
7. **Do not begin Step 2.4 before Step 2.3 is committed and tested** — the test must use the builder before the constructor signature changes.

---

## Phase 1 — Stabilization

*Safe, isolated, zero-risk changes. No behavior changes.*

---

### Step 1.1 — Delete placeholder Class1.cs scaffolding files

**Goal**: Remove leftover scaffolding residue from project templates.

**Files affected**:
```
src/DemoStudio.Automation.Abstractions/Class1.cs
src/DemoStudio.Automation.FlaUI/Class1.cs
src/DemoStudio.Capture.Abstractions/Class1.cs
src/DemoStudio.Desktop.Core/Class1.cs
src/DemoStudio.Domain/Class1.cs
src/DemoStudio.Infrastructure/Class1.cs
src/DemoStudio.Redaction.Abstractions/Class1.cs
```

**Exact change**: Delete all 7 files. They contain only the auto-generated empty `class Class1 {}` stub.

**Why it is safe**: Run a full solution-wide grep for `Class1` before deleting. If no references exist (expected), deletion is zero-risk. These files are not referenced by any type, namespace import, or DI registration.

**Verification**:
```
dotnet build
dotnet test
```
Expected: clean build, all tests green, no reference errors.

---

### Step 1.2 — Type the bare `catch` in `DesktopWindowCatalogService.GetProcessName`

**Goal**: Change a bare `catch` (which suppresses all exceptions including `ThreadAbortException`, `OutOfMemoryException`) to an explicit typed catch. Add a comment explaining intent.

**File affected**: `src/DemoStudio.Desktop.App/Services/DesktopWindowCatalogService.cs:169–177`

**Current code**:
```csharp
private static string GetProcessName(uint processId)
{
    try
    {
        return System.Diagnostics.Process.GetProcessById((int)processId).ProcessName;
    }
    catch
    {
        return "Unknown";
    }
}
```

**Exact change**:
```csharp
private static string GetProcessName(uint processId)
{
    try
    {
        return System.Diagnostics.Process.GetProcessById((int)processId).ProcessName;
    }
    catch (Exception)
    {
        // Process may have exited or access may be denied between window enumeration
        // and name lookup. Returning "Unknown" is an intentional safe fallback.
        return "Unknown";
    }
}
```

**Why it is safe**: Functionally identical. `catch (Exception)` catches everything a bare `catch` would catch in managed code. Zero behavior change.

**Verification**:
```
dotnet build
dotnet test
```

---

### Step 1.3 — Make `DesktopProcessRunner` required in `DesktopStartupHealthService`

**Goal**: Remove the `?? new DesktopProcessRunner()` fallback that silently bypasses DI. `DesktopProcessRunner` is already registered in `DesktopCompositionRoot` as a singleton (line 17), so DI always injects it. The fallback creates an undocumented second instance if DI fails.

**File affected**: `src/DemoStudio.Desktop.App/Services/DesktopStartupHealthService.cs:11–15`

**Current code**:
```csharp
public DesktopStartupHealthService(DesktopCaptureRuntime captureRuntime, DesktopProcessRunner? processRunner = null)
{
    _captureRuntime = captureRuntime ?? throw new ArgumentNullException(nameof(captureRuntime));
    _processRunner = processRunner ?? new DesktopProcessRunner();
}
```

**Exact change**:
```csharp
public DesktopStartupHealthService(DesktopCaptureRuntime captureRuntime, DesktopProcessRunner processRunner)
{
    _captureRuntime = captureRuntime ?? throw new ArgumentNullException(nameof(captureRuntime));
    _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
}
```

**Why it is safe**:
- `DesktopProcessRunner` is registered at `DesktopCompositionRoot.cs:17`. DI resolves it automatically.
- `DesktopStartupHealthService` is registered at `DesktopCompositionRoot.cs:84` using type-based resolution.
- No test file instantiates `DesktopStartupHealthService` directly (confirmed by reviewing all test files).

**Pre-change verification**: Grep for `new DesktopStartupHealthService` in the solution. If any call site omits `processRunner`, update it first.

**Verification**:
```
dotnet build
dotnet test
```

---

### Step 1.4 — Type the bare `catch` in `DesktopCrashReporter.TryWrite`

**Goal**: Same pattern as Step 1.2. The crash reporter swallows all exceptions silently.

**File affected**: Locate `DesktopCrashReporter.cs` — find the `TryWrite` method.

**Exact change**: Replace any bare `catch { return null; }` with:
```csharp
catch (Exception)
{
    // Crash reporter must never throw — returning null signals write failure to callers.
    return null;
}
```

**Why it is safe**: Functionally identical. Purely cosmetic typing improvement.

**Verification**:
```
dotnet build
dotnet test
```

---

### Step 1.5 — Extract `ConcurrencyConflictException` translation to a shared base class

**Goal**: Remove the repeated 3-line pattern across `DemoProjectService`, `DemoFlowService`, and `DemoExecutionService`.

**Current pattern** (identical in all 3 files):
```csharp
catch (ConcurrencyConflictException ex)
{
    throw new InvalidOperationException("...", ex);
}
```

**New file**: `src/DemoStudio.Application/Services/ApplicationServiceBase.cs`

```csharp
namespace DemoStudio.Application.Services;

internal abstract class ApplicationServiceBase
{
    protected static async Task ExecuteSaveAsync(Func<Task> saveOperation, string context)
    {
        try
        {
            await saveOperation();
        }
        catch (ConcurrencyConflictException ex)
        {
            throw new InvalidOperationException(
                $"The {context} could not be saved because related data changed during execution.", ex);
        }
    }
}
```

**Files to update after adding base class**:
- `DemoProjectService.cs` — extend `ApplicationServiceBase`, call `ExecuteSaveAsync`
- `DemoFlowService.cs` — same
- `DemoExecutionService.cs` — same

**Why it is safe**: `internal abstract` base class. No public API surface change.

**Pre-change verification**: Grep for the exact exception message strings in test files. If tests assert the exact text, keep the original message text per-service and only extract the try/catch structure.

**Verification**:
```
dotnet build
dotnet test
```

---

## Phase 2 — Dependency Injection Cleanup

*Enforcing DI boundaries. Steps must be executed sequentially — each step unlocks the next.*

---

### Step 2.1 — Mark the single-arg constructor `[Obsolete]` and document its test-only role

**Goal**: Communicate intent without any code change risk. The single-arg constructor at `MainWindowViewModel.cs:110` is **not** used by the DI container (which picks the full constructor). Document this explicitly before removing it.

**File affected**: `src/DemoStudio.Desktop.App/ViewModels/MainWindowViewModel.cs:110`

**Exact change**:
```csharp
/// <summary>
/// Convenience constructor for tests that only need a minimal ViewModel instance.
/// Production DI uses the full constructor. This constructor creates services with
/// hardcoded default paths and empty configuration — not suitable for production use.
/// </summary>
[Obsolete("Test-only. Production DI resolves via the full constructor. " +
          "Migrate tests to MainWindowViewModelTestBuilder before removing.")]
public MainWindowViewModel(RecorderSessionEngine sessionEngine)
    : this(...)
```

**Why it is safe**: Adding `[Obsolete]` generates compiler warnings but does not break anything. The one known test caller at `ReleaseConfidenceGateTests.cs:209` will emit a warning, which is intentional — it signals the migration needed in Step 2.2.

**Verification**:
```
dotnet build   # expect 1 CS0618 warning from ReleaseConfidenceGateTests.cs:209
dotnet test    # all tests still pass
```

---

### Step 2.2 — Create `MainWindowViewModelTestBuilder` in the test project

**Goal**: Replace the dependency on the single-arg constructor with a controlled test factory. This unblocks removing the constructor in Step 2.3.

**New file**: `tests/DemoStudio.Desktop.App.Tests/Helpers/MainWindowViewModelTestBuilder.cs`

```csharp
using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.App.ViewModels;
using DemoStudio.Desktop.Core.Sessions;
using DemoStudio.Desktop.Core.Time;
using DemoStudio.Infrastructure.Options;

namespace DemoStudio.Desktop.App.Tests.Helpers;

/// <summary>
/// Builds a minimally configured MainWindowViewModel for unit tests.
/// All services are real instances using temp paths.
/// If the constructor signature changes, this builder will fail to compile — update it first.
/// </summary>
internal static class MainWindowViewModelTestBuilder
{
    public static MainWindowViewModel CreateMinimal(string? tempRoot = null)
    {
        var root = tempRoot ?? Path.Combine(Path.GetTempPath(),
            "demostudio-vmtest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var options = new DesktopRecorderOptions
        {
            StorageRoot = root,
            Capture = new FfmpegCaptureOptions { FfmpegPath = "ffmpeg" }
        };

        var sessionEngine  = new RecorderSessionEngine(new SystemClock());
        var processRunner  = new DesktopProcessRunner();
        var captureRuntime = new DesktopCaptureRuntime(options);

        return new MainWindowViewModel(
            sessionEngine:              sessionEngine,
            captureRuntime:             captureRuntime,
            windowCatalogService:       new DesktopWindowCatalogService(),
            launchProfileService:       new DesktopLaunchProfileService(root),
            targetLauncher:             new DesktopTargetLauncher(),
            preflightService:           new DesktopCapturePreflightService(),
            windowFocusService:         new DesktopWindowFocusService(),
            sessionHistoryService:      new DesktopSessionHistoryService(root),
            diagnosticsBundleService:   new DesktopDiagnosticsBundleService(root),
            performanceMetricsService:  new DesktopPerformanceMetricsService(),
            smokeCheckService:          new DesktopSmokeCheckService(root, "ffmpeg"),
            composeManifestService:     new DesktopComposeManifestService(),
            videoComposeService:        new DesktopVideoComposeService(new NoOpProcessLauncher()),
            publishPackageService:      new DesktopPublishPackageService(new NoOpProcessLauncher()),
            onboardingService:          new DesktopOnboardingService(root),
            demoTemplateService:        new DesktopDemoTemplateService(root),
            sessionRecoveryService:     new DesktopSessionRecoveryService(root),
            presenterViewService:       new DesktopPresenterViewService(),
            processRunner:              processRunner,
            captureMediaCoordinator:    new DesktopCaptureMediaCoordinator(captureRuntime, processRunner),
            narrationCoordinator:       new DesktopNarrationCoordinator(captureRuntime,
                                            new DesktopClipNarrationService(),
                                            new DesktopAiNarrationService(),
                                            processRunner),
            captureWatchdogCoordinator: new DesktopCaptureWatchdogCoordinator(),
            clipCurationCoordinator:    new DesktopClipCurationCoordinator(),
            dependencyHealthService:    new DesktopDependencyHealthService(captureRuntime, processRunner),
            ffmpegOperationQueue:       new DesktopFfmpegOperationQueue());
    }
}
```

**Why it is safe**: New file in the test project only. Production code is untouched. Acts as a compile-time canary — if the constructor signature changes, this file fails to compile, preventing silent drift.

**Verification**:
```
dotnet build   # no new errors
dotnet test    # all tests still pass (new class not yet used)
```

---

### Step 2.3 — Migrate the one test that uses the single-arg constructor

**Goal**: Update `ReleaseConfidenceGateTests.cs` to use `MainWindowViewModelTestBuilder.CreateMinimal()` instead of the single-arg constructor directly.

**File affected**: `tests/DemoStudio.Desktop.App.Tests/ReleaseGate/ReleaseConfidenceGateTests.cs:209`

**Current code**:
```csharp
var vm = new MainWindowViewModel(new RecorderSessionEngine(new SystemClock()));
```

**Exact change**:
```csharp
// Add using at top of file:
using DemoStudio.Desktop.App.Tests.Helpers;

// Replace the instantiation:
var vm = MainWindowViewModelTestBuilder.CreateMinimal();
```

**Why it is safe**: `CreateMinimal()` produces a ViewModel that behaves identically for this test. The test only calls `RunStartCountdownAsync` via reflection — a pure countdown timer unaffected by service configuration.

**Verification**:
```
dotnet test --filter "CountdownCancellation_ReturnsFalseQuickly"
dotnet test   # full suite
```
Expected: same result (false, elapsed < 1s), zero regression.

---

### Step 2.4 — Remove the single-arg constructor from `MainWindowViewModel`

**Goal**: Remove the constructor that creates 18+ services with hardcoded defaults, now that no caller uses it.

**File affected**: `src/DemoStudio.Desktop.App/ViewModels/MainWindowViewModel.cs:110–133`

**Pre-change check**: Grep the entire solution for `new MainWindowViewModel(` — confirm zero references remain after Step 2.3.

**Exact change**: Delete lines 110–133 inclusive (the entire single-arg constructor and its XML doc comment).

**Why it is safe**:
- `DesktopCompositionRoot` has always resolved via the full constructor (DI picks it by parameter count).
- The only test caller was migrated in Step 2.3.
- The full constructor is unchanged.

**Verification**:
```
dotnet build   # no CS0618 warnings, no CS7036 errors
dotnet test    # all tests green including ReleaseGate suite
```

---

### Step 2.5 — Make the 7 optional parameters in the full constructor required

**Goal**: Eliminate the `?? new ...` fallback instantiation inside the constructor body. All 7 services are registered in `DesktopCompositionRoot` and will be injected. The fallbacks are dead code in production.

**File affected**: `src/DemoStudio.Desktop.App/ViewModels/MainWindowViewModel.cs:154–200`

**Current parameter declarations** (lines 154–160):
```csharp
DesktopProcessRunner? processRunner = null,
DesktopCaptureMediaCoordinator? captureMediaCoordinator = null,
DesktopNarrationCoordinator? narrationCoordinator = null,
DesktopCaptureWatchdogCoordinator? captureWatchdogCoordinator = null,
DesktopClipCurationCoordinator? clipCurationCoordinator = null,
DesktopDependencyHealthService? dependencyHealthService = null,
DesktopFfmpegOperationQueue? ffmpegOperationQueue = null)
```

**Current body fallbacks** (lines 182, 195–200):
```csharp
_processRunner = processRunner ?? new DesktopProcessRunner();
_captureMediaCoordinator = captureMediaCoordinator ?? new DesktopCaptureMediaCoordinator(_captureRuntime, _processRunner);
_narrationCoordinator = narrationCoordinator ?? new DesktopNarrationCoordinator(...);
_captureWatchdogCoordinator = captureWatchdogCoordinator ?? new DesktopCaptureWatchdogCoordinator();
_clipCurationCoordinator = clipCurationCoordinator ?? new DesktopClipCurationCoordinator();
_dependencyHealthService = dependencyHealthService ?? new DesktopDependencyHealthService(...);
_ffmpegOperationQueue = ffmpegOperationQueue ?? new DesktopFfmpegOperationQueue();
```

**Exact change** — remove `?` and `= null` from params; replace `?? new ...` with guard-throws:
```csharp
DesktopProcessRunner processRunner,
DesktopCaptureMediaCoordinator captureMediaCoordinator,
DesktopNarrationCoordinator narrationCoordinator,
DesktopCaptureWatchdogCoordinator captureWatchdogCoordinator,
DesktopClipCurationCoordinator clipCurationCoordinator,
DesktopDependencyHealthService dependencyHealthService,
DesktopFfmpegOperationQueue ffmpegOperationQueue)

// In body:
_processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
_captureMediaCoordinator = captureMediaCoordinator ?? throw new ArgumentNullException(nameof(captureMediaCoordinator));
_narrationCoordinator = narrationCoordinator ?? throw new ArgumentNullException(nameof(narrationCoordinator));
_captureWatchdogCoordinator = captureWatchdogCoordinator ?? throw new ArgumentNullException(nameof(captureWatchdogCoordinator));
_clipCurationCoordinator = clipCurationCoordinator ?? throw new ArgumentNullException(nameof(clipCurationCoordinator));
_dependencyHealthService = dependencyHealthService ?? throw new ArgumentNullException(nameof(dependencyHealthService));
_ffmpegOperationQueue = ffmpegOperationQueue ?? throw new ArgumentNullException(nameof(ffmpegOperationQueue));
```

**Why it is safe**:
- All 7 types are registered in `DesktopCompositionRoot` (lines 17–35). DI injects them automatically.
- `MainWindowViewModelTestBuilder.CreateMinimal()` (Step 2.2) already passes all 7 explicitly.
- No remaining caller passes `null` for any of them after Step 2.4.

**Verification**:
```
dotnet build   # verifies DI satisfies all params
dotnet test    # verifies no runtime null-reference failures
```

---

### Step 2.6 — Document `MainWindowViewModelTestBuilder` as the canonical construction path

**Goal**: After Step 2.5 makes all params required, add a comment to the builder reinforcing that it must always be kept up to date with the constructor signature.

**File affected**: `tests/DemoStudio.Desktop.App.Tests/Helpers/MainWindowViewModelTestBuilder.cs`

**Exact change**: Add a comment above `CreateMinimal()`:
```csharp
// All constructor parameters are required. If the MainWindowViewModel constructor
// signature changes, this builder will fail to compile — update it before adding
// new service dependencies to the ViewModel.
```

**Why it is safe**: Comment-only addition. No behavior change.

---

## Phase 3 — Configuration Centralization

*Centralizes hardcoded path formula that remains in `DesktopCompositionRoot`.*

---

### Step 3.1 — Register `DesktopRuntimePaths` as a singleton to deduplicate storage root resolution

**Goal**: The 7 factory lambdas in `DesktopCompositionRoot.cs:39–83` each independently call `sp.GetRequiredService<DesktopCaptureRuntime>().StorageRoot`. Extract this into one registration.

**New file**: `src/DemoStudio.Desktop.App/Infrastructure/DesktopRuntimePaths.cs`

```csharp
namespace DemoStudio.Desktop.App.Infrastructure;

/// <summary>
/// Resolved runtime paths derived from DesktopCaptureRuntime at startup.
/// Registered as a singleton to avoid re-resolving DesktopCaptureRuntime
/// in every storage-dependent service factory.
/// </summary>
internal sealed record DesktopRuntimePaths(string StorageRoot, string FfmpegPath);
```

**Update `DesktopCompositionRoot.cs`** — add one registration after `DesktopCaptureRuntime`:
```csharp
services.AddSingleton(sp =>
{
    var runtime = sp.GetRequiredService<DesktopCaptureRuntime>();
    return new DesktopRuntimePaths(runtime.StorageRoot, runtime.FfmpegPath);
});
```

Then replace all 7 factory lambdas to use `DesktopRuntimePaths`:
```csharp
services.AddSingleton(sp =>
{
    var paths = sp.GetRequiredService<DesktopRuntimePaths>();
    return new DesktopRuntimeLogService(paths.StorageRoot);
});
// ... same pattern for the other 6 lambdas
```

**Why it is safe**:
- `DesktopRuntimePaths` is a new internal type — no public API impact.
- The 7 services receive the same `StorageRoot` value as before.
- Pure refactor with identical behavior.

**Verification**:
```
dotnet build
dotnet test
# Manual: launch the app and verify storage root is resolved correctly.
```

---

### Step 3.2 — Document the `StorageRoot` default in `DesktopRecorderOptionsLoader`

**Goal**: The `appsettings.json` has `"StorageRoot": ""`. Document that empty string means "use the runtime default" rather than appearing like a missing value.

**File affected**: `DesktopRecorderOptionsLoader.cs`

**Exact change**: Add a comment near the `StorageRoot` read:
```csharp
// StorageRoot: if empty or null, DesktopCaptureRuntime computes the default path as:
// %LOCALAPPDATA%\DemoStudio\RecorderDesktop
// To override, set an explicit absolute path in appsettings.json or
// via environment variable DesktopRecorder__StorageRoot.
```

**Why it is safe**: Comment-only addition. Zero behavior change.

---

### Step 3.3 — Create `DesktopStoragePaths` static helper

**Goal**: Centralize the default path formula so future callers (including tests) do not duplicate it.

**New file**: `src/DemoStudio.Desktop.App/Infrastructure/DesktopStoragePaths.cs`

```csharp
namespace DemoStudio.Desktop.App.Infrastructure;

/// <summary>
/// Canonical path computations for DemoStudio desktop storage locations.
/// All code that needs the recorder storage root should call this helper
/// rather than duplicating the path formula inline.
/// </summary>
internal static class DesktopStoragePaths
{
    private const string AppFolder = "DemoStudio";
    private const string RecorderSubfolder = "RecorderDesktop";

    /// <summary>
    /// Returns the default recorder storage root:
    /// %LOCALAPPDATA%\DemoStudio\RecorderDesktop
    /// </summary>
    public static string GetDefaultRecorderRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolder,
            RecorderSubfolder);
}
```

**Update `MainWindowViewModelTestBuilder`** to use the canonical helper for its fallback:
```csharp
// Replace the hardcoded fallback path with the canonical helper:
var root = tempRoot ?? DesktopStoragePaths.GetDefaultRecorderRoot();
```

**Why it is safe**: New internal file. Single call site at this point. Centralizes one formula that would otherwise be duplicated.

**Verification**:
```
dotnet build
dotnet test
```

---

## Phase 4 — ViewModel Decomposition

*The largest structural change. Execute one partial at a time. Each step is independently reversible.*

> **Design rule**: Each extracted ViewModel follows the same pattern already established by `OnboardingViewModel`, `SessionHistoryViewModel`, `TargetingLaunchViewModel`, etc. `MainWindowViewModel` retains a field reference and wires `PropertyChanged` exactly as it does for existing child ViewModels.

> **Blocked until**: Phase 2 is fully complete and all tests are green.

---

### Step 4.1 — Extract `HealthMonitorViewModel` from `MainWindowViewModel.RuntimeHealth.cs`

**Goal**: The `RuntimeHealth` partial manages telemetry, dependency health, performance budgets, and timer orchestration. It accesses `_dependencyHealthService`, `_performanceMetricsService`, `_smokeCheckService`, and `_captureRuntime`. These can be fully encapsulated.

**Files affected**:
- New: `src/DemoStudio.Desktop.App/ViewModels/HealthMonitorViewModel.cs`
- Modified: `src/DemoStudio.Desktop.App/ViewModels/MainWindowViewModel.cs` (add `private readonly HealthMonitorViewModel _healthMonitor` field)
- Modified: `src/DemoStudio.Desktop.App/ViewModels/MainWindowViewModel.RuntimeHealth.cs` (delegate all calls to `_healthMonitor`)
- Modified: `src/DemoStudio.Desktop.App/DesktopCompositionRoot.cs` (register `HealthMonitorViewModel`)

**Steps within this step**:
1. Create `HealthMonitorViewModel` as a standalone class with its own `INotifyPropertyChanged` and injected services.
2. In `MainWindowViewModel`, add `private readonly HealthMonitorViewModel _healthMonitor` and initialize it in the full constructor.
3. In `MainWindowViewModel.RuntimeHealth.cs`, replace all implementation with delegation to `_healthMonitor`.
4. Expose `HealthMonitorViewModel` as a public property for XAML binding if needed.
5. Register `HealthMonitorViewModel` in `DesktopCompositionRoot`.

**Pre-step requirement**: Read and fully map every public property in `MainWindowViewModel.RuntimeHealth.cs`. Verify none are accessed from other partials before extraction.

**Why it is safe**: The partial class pattern already cleanly separates the code. Public property names and XAML binding paths are preserved (or forwarded) — UI does not change.

**Verification**:
```
dotnet build
dotnet test
# Manual: run the app and confirm the dependency health panel renders correctly.
```

---

### Step 4.2 — Extract `PublishWorkflowViewModel` from `MainWindowViewModel.PublishWorkflow.cs`

**Goal**: Publishing logic (`_publishPackageService`, publish state, export style) is entirely self-contained after a session completes.

**Files affected**:
- New: `src/DemoStudio.Desktop.App/ViewModels/PublishWorkflowViewModel.cs`
- Modified: `MainWindowViewModel.cs`, `MainWindowViewModel.PublishWorkflow.cs`
- Modified: `DesktopCompositionRoot.cs`

**Same pattern as Step 4.1.** Read all public properties in the partial first. Map which ones cross-reference state in other partials — those become constructor parameters or `PropertyChanged` subscriptions.

**Verification**: Same as Step 4.1. Manual: verify publish workflow completes end-to-end.

---

### Step 4.3 — Extract `TemplateManagementViewModel` from `MainWindowViewModel.Templates.cs`

**Goal**: Demo template save/apply/delete is entirely self-contained around `_demoTemplateService`.

**Files affected**:
- New: `src/DemoStudio.Desktop.App/ViewModels/TemplateManagementViewModel.cs`
- Modified: `MainWindowViewModel.cs`, `MainWindowViewModel.Templates.cs`
- Modified: `DesktopCompositionRoot.cs`

---

### Step 4.4 — Extract `SessionRecoveryViewModel` from `MainWindowViewModel.Recovery.cs`

**Goal**: Draft state save/restore is self-contained around `_sessionRecoveryService`.

**Note**: Recovery may publish state changes back to `MainWindowViewModel` — use `PropertyChanged` event forwarding as the current `OnboardingViewModel` pattern demonstrates.

---

> **Steps 4.5–4.8** (History, Targeting, Capture, Stage) follow the same pattern.
> Each is deferred until the preceding step is verified stable.
> Do not begin the next partial extraction until the previous one has passed a full test run and a manual app smoke test.

---

## Phase 5 — Service Structure Cleanup

*Minor housekeeping. Safe at any point after Phase 1.*

---

### Step 5.1 — Verify `ApplicationServiceBase` adoption (follow-up from Step 1.5)

**Goal**: Confirm all 3 services extend `ApplicationServiceBase` and route through `ExecuteSaveAsync`. Verify unit tests covering the concurrency path still pass.

**Files affected**: `DemoProjectService.cs`, `DemoFlowService.cs`, `DemoExecutionService.cs`

---

### Step 5.2 — Add structured logging to `DesktopWindowCatalogService`

**Goal**: After Step 1.2 typed the catch block, add actual diagnostic output for failed process name lookups.

**File affected**: `src/DemoStudio.Desktop.App/Services/DesktopWindowCatalogService.cs`

**Exact change**: Inject `DesktopRuntimeLogService` (or equivalent logging abstraction) via constructor. In the catch block:
```csharp
catch (Exception ex)
{
    // Log at debug level — this is a frequent, benign failure during window enumeration.
    _log.Write("WindowCatalog", $"Could not resolve process name for PID {processId}: {ex.Message}");
    return "Unknown";
}
```

**Why it is safe**: Additive only. Requires registering the logger in `DesktopCompositionRoot` — it is already registered.

**Verification**:
```
dotnet build
dotnet test
```

---

## Summary Table

| Step | Phase | File(s) | Risk | Effort |
|---|---|---|---|---|
| 1.1 | Stabilization | 7 × Class1.cs | Zero | 5 min |
| 1.2 | Stabilization | DesktopWindowCatalogService.cs | Zero | 5 min |
| 1.3 | Stabilization | DesktopStartupHealthService.cs | Very Low | 10 min |
| 1.4 | Stabilization | DesktopCrashReporter.cs | Zero | 5 min |
| 1.5 | Stabilization | DemoProjectService + 2 others | Low | 30 min |
| 2.1 | DI Cleanup | MainWindowViewModel.cs | Zero | 5 min |
| 2.2 | DI Cleanup | New test helper file | Zero | 30 min |
| 2.3 | DI Cleanup | ReleaseConfidenceGateTests.cs | Low | 10 min |
| 2.4 | DI Cleanup | MainWindowViewModel.cs | Low | 15 min |
| 2.5 | DI Cleanup | MainWindowViewModel.cs | Medium | 20 min |
| 2.6 | DI Cleanup | TestBuilder (comment only) | Zero | 5 min |
| 3.1 | Config | DesktopCompositionRoot.cs + new file | Low | 30 min |
| 3.2 | Config | DesktopRecorderOptionsLoader.cs | Zero | 5 min |
| 3.3 | Config | New infrastructure file + TestBuilder | Zero | 15 min |
| 4.1 | ViewModel | RuntimeHealth → HealthMonitorViewModel | Medium | 2–4 hrs |
| 4.2 | ViewModel | PublishWorkflow partial | Medium | 2–4 hrs |
| 4.3 | ViewModel | Templates partial | Medium | 1–2 hrs |
| 4.4 | ViewModel | Recovery partial | Medium | 1–2 hrs |
| 5.1 | Cleanup | ApplicationServiceBase | Low | 30 min |
| 5.2 | Cleanup | DesktopWindowCatalogService.cs | Low | 20 min |
