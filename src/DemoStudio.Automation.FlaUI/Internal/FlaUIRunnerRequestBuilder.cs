namespace DemoStudio.Automation.FlaUI.Internal;

using DemoStudio.Automation.Abstractions.Interfaces;
using DemoStudio.Automation.FlaUI.Models;
using DemoStudio.Automation.FlaUI.Options;
using DemoStudio.Application.Services;
using DemoStudio.Domain.Entities;
using DemoStudio.Domain.Enums;
using System.Text.Json;

internal static class FlaUIRunnerRequestBuilder
{
    internal static FlaUIRunnerRequest BuildForExecution(AutomationExecutionRequest request, FlaUIRunnerOptions options)
    {
        var applicationPath = ResolveApplicationPath(request.Target.TargetReference);
        return new FlaUIRunnerRequest(
            1,
            request.Run.Id.ToString("D"),
            applicationPath,
            ResolveWorkingDirectory(applicationPath),
            string.Empty,
            options.WindowFindTimeoutMs,
            request.Steps.OrderBy(x => x.Sequence).Select(MapStep).ToArray());
    }

    internal static FlaUIRunnerRequest BuildForInspection(ApplicationTarget target, FlaUIRunnerOptions options)
    {
        var applicationPath = ResolveApplicationPath(target.TargetReference);
        return new FlaUIRunnerRequest(
            1,
            Guid.NewGuid().ToString("D"),
            applicationPath,
            ResolveWorkingDirectory(applicationPath),
            string.Empty,
            options.WindowFindTimeoutMs,
            Array.Empty<FlaUIRunnerStep>());
    }

    internal static string ResolveWorkingDirectory(string applicationPath)
    {
        var directory = Path.GetDirectoryName(applicationPath);
        return string.IsNullOrWhiteSpace(directory) ? Environment.CurrentDirectory : directory;
    }

    private static string ResolveApplicationPath(string targetReference)
    {
        return RuntimePathResolver.TryResolveDesktopExecutable(targetReference, out var resolvedPath)
            ? resolvedPath
            : targetReference;
    }

    private static FlaUIRunnerStep MapStep(FlowStep step)
    {
        if (step.ActionKey.Equals("Launch", StringComparison.OrdinalIgnoreCase))
        {
            return new FlaUIRunnerStep("Launch", null, null, null, null);
        }

        if (step.ActionKey.Equals("WaitForWindow", StringComparison.OrdinalIgnoreCase))
        {
            return new FlaUIRunnerStep(
                "WaitForWindow",
                GetPayloadString(step.PayloadJson, "titleContains"),
                null,
                null,
                null);
        }

        if (step.ActionKey.Equals("CloseApplication", StringComparison.OrdinalIgnoreCase))
        {
            return new FlaUIRunnerStep("CloseApplication", null, null, null, null);
        }

        return step.StepType switch
        {
            FlowStepType.Click => new FlaUIRunnerStep("Click", null, GetPayloadString(step.PayloadJson, "automationId") ?? step.ActionKey, null, null),
            FlowStepType.InputText => new FlaUIRunnerStep(
                "Type",
                null,
                GetPayloadString(step.PayloadJson, "automationId") ?? step.ActionKey,
                GetPayloadString(step.PayloadJson, "value") ?? string.Empty,
                null),
            FlowStepType.Wait => new FlaUIRunnerStep("Wait", null, null, null, GetPayloadInt(step.PayloadJson, "ms") ?? (step.TimeoutSeconds * 1000)),
            _ => new FlaUIRunnerStep("Wait", null, null, null, Math.Max(250, step.TimeoutSeconds * 1000))
        };
    }

    private static string? GetPayloadString(string? payloadJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }
        catch
        {
        }

        return null;
    }

    private static int? GetPayloadInt(string? payloadJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch
        {
        }

        return null;
    }
}
