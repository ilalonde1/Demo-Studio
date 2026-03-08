namespace DemoStudio.Automation.FlaUI.Internal;

internal static class RunnerFilePathBuilder
{
    internal static (string RequestPath, string ResponsePath) Build(string outputDirectory, Guid runId)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var requestPath = Path.Combine(outputDirectory, $"flaui-request-{runId:N}-{suffix}.json");
        var responsePath = Path.Combine(outputDirectory, $"flaui-response-{runId:N}-{suffix}.json");
        return (requestPath, responsePath);
    }
}

