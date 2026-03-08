using DemoStudio.Infrastructure.Execution.Windows;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopWindowLocator : IWindowLocator
{
    private readonly IWindowLocator _inner = WindowLocatorFactory.CreateDefault();

    public Task<WindowLocatorResult> FindAsync(WindowLocatorRequest request, CancellationToken cancellationToken = default)
        => _inner.FindAsync(request, cancellationToken);
}
