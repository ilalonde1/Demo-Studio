using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.Services;

internal sealed class DesktopRuntimeLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly DesktopRuntimeLogService _runtimeLog;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    public DesktopRuntimeLoggerProvider(DesktopRuntimeLogService runtimeLog)
    {
        _runtimeLog = runtimeLog;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new DesktopRuntimeLogger(_runtimeLog, categoryName, () => _scopeProvider);
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public void Dispose()
    {
    }

    private sealed class DesktopRuntimeLogger : ILogger
    {
        private readonly DesktopRuntimeLogService _runtimeLog;
        private readonly string _categoryName;
        private readonly Func<IExternalScopeProvider> _scopeProviderAccessor;

        public DesktopRuntimeLogger(
            DesktopRuntimeLogService runtimeLog,
            string categoryName,
            Func<IExternalScopeProvider> scopeProviderAccessor)
        {
            _runtimeLog = runtimeLog;
            _categoryName = categoryName;
            _scopeProviderAccessor = scopeProviderAccessor;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return _scopeProviderAccessor().Push(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            _runtimeLog.Log(logLevel, _categoryName, message, exception);
        }
    }
}
