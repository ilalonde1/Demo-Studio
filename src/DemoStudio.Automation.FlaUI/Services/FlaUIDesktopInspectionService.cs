namespace DemoStudio.Automation.FlaUI.Services;

using System.Text;
using DemoStudio.Application.Abstractions.System;
using DemoStudio.Application.Services;
using DemoStudio.Automation.FlaUI.Internal;
using DemoStudio.Automation.FlaUI.Options;
using DemoStudio.Domain.Entities;
using Microsoft.Extensions.Options;

public sealed class FlaUIDesktopInspectionService : IDesktopInspectionService
{
    private readonly IProcessLauncher _processLauncher;
    private readonly IFileStorage _fileStorage;
    private readonly FlaUIRunnerOptions _options;

    public FlaUIDesktopInspectionService(
        IProcessLauncher processLauncher,
        IFileStorage fileStorage,
        IOptions<FlaUIRunnerOptions> options)
    {
        _processLauncher = processLauncher;
        _fileStorage = fileStorage;
        _options = options.Value;
    }

    public async Task<ElementInspectionResult> InspectAsync(ApplicationTarget target, CancellationToken cancellationToken = default)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        try
        {
            var outputDirectory = Path.Combine(Path.GetTempPath(), "DemoStudio", "FlaUI", "inspect", Guid.NewGuid().ToString("N"));
            var (requestPath, responsePath) = RunnerFilePathBuilder.Build(outputDirectory, Guid.NewGuid());

            var payload = FlaUIRunnerRequestBuilder.BuildForInspection(target, _options);
            var requestJson = RunnerPayloadSerializer.SerializeRequest(payload);
            await using (var requestStream = new MemoryStream(Encoding.UTF8.GetBytes(requestJson)))
            {
                await _fileStorage.SaveAsync(requestPath, requestStream, cancellationToken);
            }

            var runnerExePath = ResolveRunnerExecutablePath(_options.RunnerExePath, outputDirectory);
            var arguments = RunnerCommandBuilder.BuildArguments(requestPath, responsePath, inspectMode: true);
            var workingDirectory = Path.GetDirectoryName(runnerExePath) ?? Environment.CurrentDirectory;

            var launchResult = await _processLauncher.LaunchAsync(
                new ProcessLaunchRequest(
                    runnerExePath,
                    string.Empty,
                    workingDirectory,
                    arguments,
                    "flaui-runner-inspection",
                    target.Id.ToString("N")),
                cancellationToken);

            if (!launchResult.Started)
            {
                return new ElementInspectionResult(false, launchResult.ErrorMessage ?? "Inspection runner failed to start.", null, Array.Empty<string>());
            }

            var execution = launchResult.Execution;
            if (execution?.Cancelled == true)
            {
                return new ElementInspectionResult(false, "Inspection cancelled.", null, Array.Empty<string>());
            }

            if (execution?.TimedOut == true)
            {
                return new ElementInspectionResult(false, "Inspection timed out.", null, Array.Empty<string>());
            }

            var responseExists = await _fileStorage.ExistsAsync(responsePath, cancellationToken);
            if (!responseExists)
            {
                var diagnostic = execution is null
                    ? "Inspection runner did not return a response."
                    : $"Inspection runner exited with code {execution.ExitCode}.";
                return new ElementInspectionResult(false, diagnostic, null, Array.Empty<string>());
            }

            var responseJson = await _fileStorage.ReadAllTextAsync(responsePath, cancellationToken);
            var response = RunnerPayloadSerializer.DeserializeResponse(responseJson);
            if (response is null)
            {
                return new ElementInspectionResult(false, "Inspection response payload was invalid.", null, Array.Empty<string>());
            }

            var element = response.InspectionElement is null
                ? null
                : new InspectedElementMetadata(
                    response.InspectionElement.AutomationId,
                    response.InspectionElement.Name,
                    response.InspectionElement.ControlType,
                    response.InspectionElement.BoundingRectangle,
                    response.InspectionElement.WindowTitle);

            return new ElementInspectionResult(response.Succeeded, response.DiagnosticMessage, element, response.LogLines);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ElementInspectionResult(false, "Inspection cancelled.", null, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            return new ElementInspectionResult(false, ex.Message, null, Array.Empty<string>());
        }
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
}
