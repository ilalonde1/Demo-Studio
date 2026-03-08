namespace DemoStudio.Application.Services;

public static class RunFailureClassifier
{
    public static RunFailureDiagnostic FromReason(string? reason)
    {
        var message = string.IsNullOrWhiteSpace(reason) ? "Run failed." : reason.Trim();
        if (TryExtractTaggedCode(message, out var taggedCode, out var taggedMessage))
        {
            return new RunFailureDiagnostic(taggedCode, taggedMessage);
        }

        if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return new RunFailureDiagnostic("DS-RUN-101", message);
        }

        if (message.Contains("has no application target configured", StringComparison.OrdinalIgnoreCase))
        {
            return new RunFailureDiagnostic("DS-RUN-102", message);
        }

        if (message.Contains("start process", StringComparison.OrdinalIgnoreCase)
            || message.Contains("directory name is invalid", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase))
        {
            return new RunFailureDiagnostic("DS-RUN-103", message);
        }

        if (message.Contains("node", StringComparison.OrdinalIgnoreCase))
        {
            return new RunFailureDiagnostic("DS-RUN-104", message);
        }

        if (message.Contains("runner script", StringComparison.OrdinalIgnoreCase)
            || message.Contains("runner executable", StringComparison.OrdinalIgnoreCase))
        {
            return new RunFailureDiagnostic("DS-RUN-105", message);
        }

        if (message.Contains("preflight", StringComparison.OrdinalIgnoreCase))
        {
            return new RunFailureDiagnostic("DS-RUN-120", message);
        }

        if (message.Contains("raw video file", StringComparison.OrdinalIgnoreCase)
            && message.Contains("empty", StringComparison.OrdinalIgnoreCase))
        {
            return new RunFailureDiagnostic("DS-RUN-106", message);
        }

        if (message.Contains("capture start", StringComparison.OrdinalIgnoreCase))
        {
            return new RunFailureDiagnostic("DS-RUN-107", message);
        }

        if (message.Contains("capture stop", StringComparison.OrdinalIgnoreCase))
        {
            return new RunFailureDiagnostic("DS-RUN-108", message);
        }

        if (message.Contains("FAIL_STEP", StringComparison.OrdinalIgnoreCase)
            || message.Contains("automation execution failed", StringComparison.OrdinalIgnoreCase))
        {
            return new RunFailureDiagnostic("DS-RUN-109", message);
        }

        if (message.Contains("redaction", StringComparison.OrdinalIgnoreCase))
        {
            return new RunFailureDiagnostic("DS-RUN-110", message);
        }

        if (message.Contains("watchdog", StringComparison.OrdinalIgnoreCase)
            || message.Contains("max duration", StringComparison.OrdinalIgnoreCase)
            || message.Contains("operation was canceled", StringComparison.OrdinalIgnoreCase)
            || message.Contains("execution was cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return new RunFailureDiagnostic("DS-RUN-130", message);
        }

        if (message.Contains("cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return new RunFailureDiagnostic("DS-RUN-111", message);
        }

        if (message.Contains("force-stopped", StringComparison.OrdinalIgnoreCase)
            || message.Contains("operator", StringComparison.OrdinalIgnoreCase))
        {
            return new RunFailureDiagnostic("DS-RUN-112", message);
        }

        return new RunFailureDiagnostic("DS-RUN-900", message);
    }

    public static RunFailureDiagnostic FromException(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var diagnostic = FromReason(ex.Message);
        return diagnostic.Code == "DS-RUN-900"
            ? new RunFailureDiagnostic("DS-RUN-999", diagnostic.Message)
            : diagnostic;
    }

    public static string FormatTaggedReason(string? reason)
    {
        var diagnostic = FromReason(reason);
        return diagnostic.Formatted;
    }

    private static bool TryExtractTaggedCode(string message, out string code, out string cleanMessage)
    {
        code = string.Empty;
        cleanMessage = message;
        if (message.Length < 11 || message[0] != '[')
        {
            return false;
        }

        var close = message.IndexOf(']');
        if (close <= 1)
        {
            return false;
        }

        var candidate = message[1..close].Trim();
        if (!candidate.StartsWith("DS-RUN-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        code = candidate.ToUpperInvariant();
        cleanMessage = message[(close + 1)..].TrimStart();
        if (string.IsNullOrWhiteSpace(cleanMessage))
        {
            cleanMessage = "Run failed.";
        }

        return true;
    }
}

public sealed record RunFailureDiagnostic(string Code, string Message)
{
    public string Formatted => $"[{Code}] {Message}";
}
