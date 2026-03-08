namespace DemoStudio.Application.Abstractions.System;

public interface IApplicationProcessInspector
{
    Task<bool> IsRunningAsync(string applicationPathOrReference, CancellationToken cancellationToken = default);
}
