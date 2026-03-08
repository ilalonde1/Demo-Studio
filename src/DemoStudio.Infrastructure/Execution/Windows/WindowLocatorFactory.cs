namespace DemoStudio.Infrastructure.Execution.Windows;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public static class WindowLocatorFactory
{
    public static IWindowLocator CreateDefault(ILoggerFactory? loggerFactory = null)
    {
        var logger = loggerFactory?.CreateLogger<Win32WindowLocator>() ?? NullLogger<Win32WindowLocator>.Instance;
        return new Win32WindowLocator(new Win32WindowEnumerator(), logger);
    }
}
