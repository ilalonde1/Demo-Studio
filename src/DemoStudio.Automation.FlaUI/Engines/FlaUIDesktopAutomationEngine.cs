namespace DemoStudio.Automation.FlaUI.Engines;

using System.Text;
using DemoStudio.Application.Abstractions.System;
using DemoStudio.Application.Services;
using DemoStudio.Automation.Abstractions.Interfaces;
using DemoStudio.Automation.FlaUI.Internal;
using DemoStudio.Automation.FlaUI.Models;
using DemoStudio.Automation.FlaUI.Options;
using Microsoft.Extensions.Options;

public sealed class FlaUIDesktopAutomationEngine : IDesktopAutomationEngine
{
    private readonly IProcessLauncher _processLauncher;
    private readonly IFileStorage _fileStorage;
    private readonly FlaUIRunnerOptions _options;

    public FlaUIDesktopAutomationEngine(
        IProcessLauncher processLauncher,
        IFileStorage fileStorage,
        IOptions<FlaUIRunnerOptions> options)
    {
        _processLauncher = processLauncher;
        _fileStorage = fileStorage;
        _options = options.Value;
    }

    public async Task<AutomationExecutionResult> ExecuteAsync(AutomationExecutionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var outputDirectory = ResolveOutputDirectory(request.Run.OutputDirectory, request.Run.Id);
            var (requestPath, responsePath) = RunnerFilePathBuilder.Build(outputDirectory, request.Run.Id);

            var payload = FlaUIRunnerRequestBuilder.BuildForExecution(request, _options);

            var requestJson = RunnerPayloadSerializer.SerializeRequest(payload);
            await using (var requestStream = new MemoryStream(Encoding.UTF8.GetBytes(requestJson)))
            {
                await _fileStorage.SaveAsync(requestPath, requestStream, cancellationToken);
            }

            var runnerExePath = ResolveRunnerExecutablePath(_options.RunnerExePath, outputDirectory);
            var arguments = RunnerCommandBuilder.BuildArguments(requestPath, responsePath, inspectMode: false);
            var workingDirectory = Path.GetDirectoryName(runnerExePath) ?? Environment.CurrentDirectory;

            var launchResult = await _processLauncher.LaunchAsync(
                new ProcessLaunchRequest(runnerExePath, arguments, workingDirectory),
                cancellationToken);

            if (!launchResult.Started)
            {
                return new AutomationExecutionResult(false, Array.Empty<string>(), launchResult.ErrorMessage ?? "FlaUI runner failed to start.");
            }

            var execution = launchResult.Execution;
            if (execution?.Cancelled == true)
            {
                return new AutomationExecutionResult(false, Array.Empty<string>(), "Cancelled");
            }

            if (execution?.TimedOut == true)
            {
                return new AutomationExecutionResult(false, Array.Empty<string>(), "Timed out");
            }

            var responseExists = await _fileStorage.ExistsAsync(responsePath, cancellationToken);
            if (!responseExists)
            {
                var diagnostic = execution is not null
                    ? BuildExitDiagnostic(execution)
                    : "FlaUI runner did not produce a response file.";
                return new AutomationExecutionResult(false, Array.Empty<string>(), diagnostic);
            }

            var responseJson = await _fileStorage.ReadAllTextAsync(responsePath, cancellationToken);
            var response = RunnerPayloadSerializer.DeserializeResponse(responseJson);
            if (response is null)
            {
                var stderrPrefix = Truncate(execution?.StdErr ?? string.Empty, 2000);
                var suffix = string.IsNullOrWhiteSpace(stderrPrefix) ? string.Empty : $" stderr: {stderrPrefix}";
                return new AutomationExecutionResult(false, Array.Empty<string>(), $"FlaUI runner returned an invalid response payload.{suffix}");
            }

            var exitCode = execution?.ExitCode ?? 0;
            if (exitCode != 0 && response.Succeeded)
            {
                return new AutomationExecutionResult(false, response.LogLines, BuildExitDiagnostic(execution!));
            }

            return new AutomationExecutionResult(response.Succeeded, response.LogLines, response.DiagnosticMessage);
        }
        catch (OperationCanceledException)
        {
            return new AutomationExecutionResult(false, Array.Empty<string>(), "FlaUI execution was cancelled.");
        }
        catch (Exception ex)
        {
            return new AutomationExecutionResult(false, Array.Empty<string>(), $"FlaUI execution failed: {ex.Message}");
        }
    }

    private static string ResolveOutputDirectory(string? runOutputDirectory, Guid runId)
    {
        if (!string.IsNullOrWhiteSpace(runOutputDirectory))
        {
            return Path.GetFullPath(runOutputDirectory);
        }

        return Path.Combine(Path.GetTempPath(), "DemoStudio", "FlaUI", runId.ToString("N"));
    }

    private static string ResolveRunnerExecutablePath(string configuredPath, string? anchorPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException("FlaUI runner executable path is not configured.");
        }

        if (RuntimePathResolver.TryResolveFile(configuredPath, out var resolvedPath, anchorPath))
        {
            return resolvedPath;
        }

        var searched = new List<string>();
        foreach (var baseDirectory in RuntimePathResolver.EnumerateCandidateBaseDirectories(anchorPath))
        {
            var candidate = Path.GetFullPath(Path.Combine(baseDirectory, configuredPath));
            searched.Add(candidate);
        }

        throw new FileNotFoundException(
            $"FlaUI runner executable '{configuredPath}' was not found. Checked: {string.Join("; ", searched.Distinct(StringComparer.OrdinalIgnoreCase))}");
    }


    private static string BuildExitDiagnostic(ProcessExecutionResult execution)
    {
        var stderr = Truncate(execution.StdErr ?? string.Empty, 2000);
        var suffix = string.IsNullOrWhiteSpace(stderr) ? string.Empty : $" {stderr}";
        return execution.ExitCode switch
        {
            0 => "FlaUI runner completed successfully.",
            2 => $"FlaUI runner reported automation failure.{suffix}".Trim(),
            64 => $"FlaUI runner reported usage/configuration error.{suffix}".Trim(),
            _ => $"FlaUI runner exited with code {execution.ExitCode}.{suffix}".Trim()
        };
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
