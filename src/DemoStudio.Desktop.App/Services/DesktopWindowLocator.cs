using DemoStudio.Infrastructure.Execution.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopWindowLocator : IWindowLocator
{
    private readonly IWindowLocator _inner;

    public DesktopWindowLocator()
        : this(NullLoggerFactory.Instance)
    {
    }

    public DesktopWindowLocator(ILoggerFactory loggerFactory)
    {
        _inner = WindowLocatorFactory.CreateDefault(loggerFactory);
    }

    public Task<WindowLocatorResult> FindAsync(WindowLocatorRequest request, CancellationToken cancellationToken = default)
        => _inner.FindAsync(request, cancellationToken);
}
