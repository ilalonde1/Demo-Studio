namespace DemoStudio.Infrastructure.Process;

using DemoStudio.Application.Abstractions.System;

public sealed class ApplicationProcessInspector : IApplicationProcessInspector
{
    public Task<bool> IsRunningAsync(string applicationPathOrReference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(applicationPathOrReference))
        {
            return Task.FromResult(false);
        }

        var processName = ExtractProcessName(applicationPathOrReference);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return Task.FromResult(false);
        }

        try
        {
            var running = System.Diagnostics.Process.GetProcessesByName(processName).Length > 0;
            return Task.FromResult(running);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private static string ExtractProcessName(string value)
    {
        var trimmed = value.Trim();
        var fileName = trimmed;

        var slashIndex = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        if (slashIndex >= 0 && slashIndex < trimmed.Length - 1)
        {
            fileName = trimmed[(slashIndex + 1)..];
        }

        var dotIndex = fileName.LastIndexOf('.');
        if (dotIndex > 0)
        {
            fileName = fileName[..dotIndex];
        }

        return fileName.Trim();
    }
}
