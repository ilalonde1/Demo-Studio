namespace DemoStudio.Application.Services;

using System.Diagnostics;
using System.Text;
using DemoStudio.Application.Abstractions.Persistence;
using DemoStudio.Application.Abstractions.System;
using DemoStudio.Automation.Abstractions.Interfaces;
using DemoStudio.Capture.Abstractions.Interfaces;
using DemoStudio.Redaction.Abstractions.Interfaces;
using DemoStudio.Domain.Enums;
using Microsoft.Extensions.Logging;

public sealed class DemoRunPipeline : IDemoRunPipeline
{
    private readonly IAutomationRuntimeGate _runtimeGate;
    private readonly IAutomationEngineResolver _engineResolver;
    private readonly IVideoCaptureService _videoCaptureService;
    private readonly IRedactionProcessor _redactionProcessor;
    private readonly IDemoOutputPathProvider _outputPathProvider;
    private readonly IFileStorage _fileStorage;
    private readonly ITimelineMarkerWriter _timelineMarkerWriter;
    private readonly IDemoExportService _demoExportService;
    private readonly ILogger<DemoRunPipeline> _logger;

    public DemoRunPipeline(
        IAutomationRuntimeGate runtimeGate,
        IAutomationEngineResolver engineResolver,
        IVideoCaptureService videoCaptureService,
        IRedactionProcessor redactionProcessor,
        IDemoOutputPathProvider outputPathProvider,
        IFileStorage fileStorage,
        ITimelineMarkerWriter timelineMarkerWriter,
        IDemoExportService demoExportService,
        ILogger<DemoRunPipeline> logger)
    {
        _runtimeGate = runtimeGate;
        _engineResolver = engineResolver;
        _videoCaptureService = videoCaptureService;
        _redactionProcessor = redactionProcessor;
        _outputPathProvider = outputPathProvider;
        _fileStorage = fileStorage;
        _timelineMarkerWriter = timelineMarkerWriter;
        _demoExportService = demoExportService;
        _logger = logger;
    }

    public async Task<DemoRunPipelineResult> ExecuteAsync(DemoRunExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!_runtimeGate.Enabled)
        {
            return new DemoRunPipelineResult(false, string.Empty, null, null, null, "Automation runtime disabled.");
        }

        if (context.Run.Status != DemoRunStatus.Running)
        {
            throw new InvalidOperationException($"Run '{context.Run.Id}' is in status '{context.Run.Status}' and cannot be executed.");
        }

        if (context.Steps.Count == 0)
        {
            throw new InvalidOperationException($"Flow '{context.Flow.Id}' does not contain any steps.");
        }

        var orderedSteps = context.Steps.OrderBy(x => x.Sequence).ToArray();
        var paths = await _outputPathProvider.CreatePathsAsync(context.Project, context.Run, cancellationToken);

        var logLines = new List<string>
        {
            $"[{DateTimeOffset.UtcNow:u}] Run '{context.Run.Id}' started.",
            $"Project='{context.Project.Code.Value}', Flow='{context.Flow.Name}', TargetType='{context.Target.ApplicationType}'.",
            $"OutputDirectory='{paths.OutputDirectory}'."
        };

        var state = new PipelineExecutionState();
        string? persistedLogPath = null;
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["RunId"] = context.Run.Id,
            ["ProjectId"] = context.Project.Id,
            ["FlowId"] = context.Flow.Id
        });
        _logger.LogInformation("Starting demo run pipeline.");

        try
        {
            var captureStartTimer = Stopwatch.StartNew();
            await StartCaptureAsync(context, paths, logLines, state, cancellationToken);
            captureStartTimer.Stop();
            _logger.LogDebug("Pipeline phase CaptureStart completed in {ElapsedMs} ms.", captureStartTimer.ElapsedMilliseconds);

            var automationTimer = Stopwatch.StartNew();
            await ExecuteAutomationAsync(context, orderedSteps, paths.OutputDirectory, logLines, state, cancellationToken);
            automationTimer.Stop();
            _logger.LogDebug("Pipeline phase Automation completed in {ElapsedMs} ms.", automationTimer.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetFailure(state, "Execution was cancelled.");
            logLines.Add($"Pipeline failed: {state.FailureReason}");
            _logger.LogWarning("Run {RunId} pipeline was cancelled.", context.Run.Id);
        }
        catch (Exception ex)
        {
            SetFailure(state, ex.Message);
            logLines.Add($"Pipeline failed: {state.FailureReason}");
            _logger.LogError(ex, "Run {RunId} pipeline failed unexpectedly.", context.Run.Id);
        }
        finally
        {
            var captureStopTimer = Stopwatch.StartNew();
            await RunCleanupStepAsync(
                token => StopCaptureAsync(context, paths.OutputDirectory, logLines, state, token),
                cancellationToken);
            captureStopTimer.Stop();
            _logger.LogDebug("Pipeline phase CaptureStop completed in {ElapsedMs} ms.", captureStopTimer.ElapsedMilliseconds);

            var redactionTimer = Stopwatch.StartNew();
            await RunCleanupStepAsync(
                token => RunRedactionAsync(context, paths, logLines, state, token),
                cancellationToken);
            redactionTimer.Stop();
            _logger.LogDebug("Pipeline phase Redaction completed in {ElapsedMs} ms.", redactionTimer.ElapsedMilliseconds);

            var exportTimer = Stopwatch.StartNew();
            await RunCleanupStepAsync(
                token => RunExportAsync(context, paths, orderedSteps, logLines, state, token),
                cancellationToken);
            exportTimer.Stop();
            _logger.LogDebug("Pipeline phase Export completed in {ElapsedMs} ms.", exportTimer.ElapsedMilliseconds);

            logLines.Add($"[{DateTimeOffset.UtcNow:u}] Run '{context.Run.Id}' completed.");
            try
            {
                var persistLogsTimer = Stopwatch.StartNew();
                persistedLogPath = await RunCleanupStepAsync(
                    token => PersistLogsAsync(paths.LogPath, logLines, token),
                    cancellationToken);
                persistLogsTimer.Stop();
                _logger.LogDebug("Pipeline phase PersistLogs completed in {ElapsedMs} ms.", persistLogsTimer.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                SetFailure(state, $"Failed to persist log file: {ex.Message}");
                _logger.LogError(ex, "Run {RunId} failed to persist pipeline logs.", context.Run.Id);
            }
        }

        _logger.LogInformation(
            "Demo run pipeline completed. Succeeded={Succeeded}, FailureReason={FailureReason}",
            state.FailureReason is null,
            state.FailureReason);

        return new DemoRunPipelineResult(
            state.FailureReason is null,
            paths.OutputDirectory,
            state.RawVideoPath,
            state.RedactedVideoPath,
            persistedLogPath,
            state.FailureReason);
    }

    private async Task RunExportAsync(
        DemoRunExecutionContext context,
        DemoRunOutputPaths paths,
        IReadOnlyCollection<Domain.Entities.FlowStep> orderedSteps,
        List<string> logLines,
        PipelineExecutionState state,
        CancellationToken cancellationToken)
    {
        if (state.FailureReason is not null || string.IsNullOrWhiteSpace(state.RawVideoPath))
        {
            return;
        }

        await WriteTimelineMarkerSafeAsync(paths.OutputDirectory, "Export", cancellationToken);
        try
        {
            var stepsExecuted = orderedSteps
                .Select(step => $"{step.Sequence:D3}:{step.StepType}:{step.ActionKey}")
                .ToArray();

            var exportResult = await _demoExportService.ExportAsync(
                new DemoExportRequest(
                    context.Run.Id,
                    context.Project.Name,
                    context.Target.ApplicationType,
                    context.Run.StartedAtUtc,
                    DateTimeOffset.UtcNow,
                    stepsExecuted,
                    paths.OutputDirectory,
                    state.RawVideoPath,
                    state.RedactedVideoPath,
                    Path.Combine(paths.OutputDirectory, "timeline.json")),
                cancellationToken);

            if (exportResult.Succeeded)
            {
                logLines.Add($"Export completed. Directory='{exportResult.ExportDirectory}'.");
            }
            else
            {
                logLines.Add("Export failed. Run status unaffected.");
            }

            foreach (var diagnostic in exportResult.Diagnostics)
            {
                logLines.Add($"Export: {diagnostic}");
            }
        }
        catch (Exception ex)
        {
            logLines.Add($"Export failed. Run status unaffected. Reason: {ex.Message}");
            _logger.LogWarning(ex, "Run {RunId} export packaging failed.", context.Run.Id);
        }
    }

    private async Task StartCaptureAsync(
        DemoRunExecutionContext context,
        DemoRunOutputPaths paths,
        List<string> logLines,
        PipelineExecutionState state,
        CancellationToken cancellationToken)
    {
        logLines.Add("=== CAPTURE START ===");
        await WriteTimelineMarkerSafeAsync(paths.OutputDirectory, "CaptureStart", cancellationToken);
        try
        {
            var captureStart = await _videoCaptureService.StartAsync(
                new CaptureStartRequest(context.Run, paths.OutputDirectory, paths.RawVideoPath),
                cancellationToken);
            if (!captureStart.Succeeded || string.IsNullOrWhiteSpace(captureStart.RawVideoPath))
            {
                throw new InvalidOperationException(captureStart.ErrorMessage ?? "Capture start failed.");
            }

            state.CaptureStarted = true;
            state.RawVideoPath = captureStart.RawVideoPath;
            logLines.Add($"Capture started. RawVideoPath='{state.RawVideoPath}'.");
        }
        catch (Exception ex)
        {
            SetFailure(state, ex.Message);
            logLines.Add($"Pipeline failed: {state.FailureReason}");
            _logger.LogError(ex, "Run {RunId} failed in pipeline.", context.Run.Id);
        }
    }

    private async Task ExecuteAutomationAsync(
        DemoRunExecutionContext context,
        IReadOnlyCollection<Domain.Entities.FlowStep> orderedSteps,
        string outputDirectory,
        List<string> logLines,
        PipelineExecutionState state,
        CancellationToken cancellationToken)
    {
        logLines.Add("=== AUTOMATION ===");
        await WriteTimelineMarkerSafeAsync(outputDirectory, "Automation", cancellationToken);

        if (!state.CaptureStarted || string.IsNullOrWhiteSpace(state.RawVideoPath))
        {
            return;
        }

        try
        {
            var engine = _engineResolver.Resolve(context.Target.ApplicationType);
            var automationResult = await engine.ExecuteAsync(
                new AutomationExecutionRequest(context.Run, context.Target, context.Flow, orderedSteps, ExecutionMode.FullRun),
                cancellationToken);
            foreach (var line in automationResult.LogLines)
            {
                logLines.Add(line);
            }

            if (!automationResult.Succeeded)
            {
                throw new InvalidOperationException(automationResult.DiagnosticMessage ?? "Automation execution failed.");
            }

            state.AutomationSucceeded = true;
        }
        catch (Exception ex)
        {
            SetFailure(state, ex.Message);
            logLines.Add($"Pipeline failed: {state.FailureReason}");
            _logger.LogError(ex, "Run {RunId} failed in pipeline.", context.Run.Id);
        }
    }

    private async Task StopCaptureAsync(
        DemoRunExecutionContext context,
        string outputDirectory,
        List<string> logLines,
        PipelineExecutionState state,
        CancellationToken cancellationToken)
    {
        logLines.Add("=== CAPTURE STOP ===");
        await WriteTimelineMarkerSafeAsync(outputDirectory, "CaptureStop", cancellationToken);

        if (!state.CaptureStarted || string.IsNullOrWhiteSpace(state.RawVideoPath))
        {
            return;
        }

        if (state.CaptureStopped)
        {
            return;
        }

        try
        {
            var captureStop = await _videoCaptureService.StopAsync(
                new CaptureStopRequest(context.Run, state.RawVideoPath),
                cancellationToken);
            if (!captureStop.Succeeded)
            {
                SetFailure(state, captureStop.ErrorMessage ?? "Capture stop failed.");
                logLines.Add($"Capture stop failed: {state.FailureReason}");
                return;
            }

            state.CaptureStopped = true;
            logLines.Add("Capture stopped.");

            var rawFileInfo = new FileInfo(state.RawVideoPath);
            if (!rawFileInfo.Exists || rawFileInfo.Length <= 0)
            {
                SetFailure(state, $"Raw video file '{state.RawVideoPath}' is empty.");
                logLines.Add($"Capture output validation failed: {state.FailureReason}");
            }
        }
        catch (Exception ex)
        {
            SetFailure(state, ex.Message);
            logLines.Add($"Capture stop threw exception: {ex.Message}");
            _logger.LogError(ex, "Run {RunId} capture stop failed.", context.Run.Id);
        }
    }

    private async Task RunRedactionAsync(
        DemoRunExecutionContext context,
        DemoRunOutputPaths paths,
        List<string> logLines,
        PipelineExecutionState state,
        CancellationToken cancellationToken)
    {
        logLines.Add("=== REDACTION ===");
        await WriteTimelineMarkerSafeAsync(paths.OutputDirectory, "Redaction", cancellationToken);

        if (!CanRunRedaction(state))
        {
            return;
        }

        if (context.RedactionRules.Count == 0)
        {
            logLines.Add("Redaction skipped: no enabled redaction rules.");
            return;
        }

        var rawExists = await _fileStorage.ExistsAsync(state.RawVideoPath!, cancellationToken);
        if (!rawExists)
        {
            SetFailure(state, $"Raw video file '{state.RawVideoPath}' was not found.");
            logLines.Add($"Pipeline failed: {state.FailureReason}");
            return;
        }

        var rawFileInfo = new FileInfo(state.RawVideoPath!);
        if (!rawFileInfo.Exists || rawFileInfo.Length <= 0)
        {
            SetFailure(state, $"Raw video file '{state.RawVideoPath}' is empty.");
            logLines.Add($"Pipeline failed: {state.FailureReason}");
            return;
        }

        try
        {
            logLines.Add("Redaction started.");
            var redactionResult = await _redactionProcessor.ProcessAsync(
                new RedactionRequest(context.Run, state.RawVideoPath!, paths.RedactedVideoPath, context.RedactionRules),
                cancellationToken);
            foreach (var line in redactionResult.LogLines)
            {
                logLines.Add(line);
            }

            if (!redactionResult.Succeeded || string.IsNullOrWhiteSpace(redactionResult.RedactedOutputPath))
            {
                throw new InvalidOperationException(redactionResult.ErrorMessage ?? "Redaction failed.");
            }

            state.RedactedVideoPath = redactionResult.RedactedOutputPath;
            logLines.Add($"Redaction completed. RedactedVideoPath='{state.RedactedVideoPath}'.");
        }
        catch (Exception ex)
        {
            SetFailure(state, ex.Message);
            logLines.Add($"Pipeline failed: {state.FailureReason}");
            _logger.LogError(ex, "Run {RunId} failed in pipeline.", context.Run.Id);
        }
    }

    private bool CanRunRedaction(PipelineExecutionState state)
    {
        return state.FailureReason is null
            && state.CaptureStarted
            && state.CaptureStopped
            && state.AutomationSucceeded
            && !string.IsNullOrWhiteSpace(state.RawVideoPath);
    }

    private async Task<string> PersistLogsAsync(string logPath, IReadOnlyCollection<string> lines, CancellationToken cancellationToken)
    {
        var content = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await _fileStorage.SaveAsync(logPath, stream, cancellationToken);
    }

    private static void SetFailure(PipelineExecutionState state, string reason)
    {
        if (state.FailureReason is null && !string.IsNullOrWhiteSpace(reason))
        {
            state.FailureReason = reason;
        }
    }

    private async Task WriteTimelineMarkerSafeAsync(string outputDirectory, string stage, CancellationToken cancellationToken)
    {
        try
        {
            await _timelineMarkerWriter.WriteStageMarkerAsync(outputDirectory, stage, DateTimeOffset.UtcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Run timeline marker write failed for stage {Stage} in {OutputDirectory}.", stage, outputDirectory);
        }
    }

    private static async Task RunCleanupStepAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            await action(cancellationToken);
            return;
        }

        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await action(cleanupTimeout.Token);
    }

    private static async Task<T> RunCleanupStepAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            return await action(cancellationToken);
        }

        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        return await action(cleanupTimeout.Token);
    }

    private sealed class PipelineExecutionState
    {
        public bool CaptureStarted { get; set; }

        public bool CaptureStopped { get; set; }

        public bool AutomationSucceeded { get; set; }

        public string? RawVideoPath { get; set; }

        public string? RedactedVideoPath { get; set; }

        public string? FailureReason { get; set; }
    }
}
