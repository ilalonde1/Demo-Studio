namespace DemoStudio.Automation.FlaUI.Internal;

internal static class RunnerCommandBuilder
{
    internal static IReadOnlyList<string> BuildArguments(string requestFilePath, string responseFilePath, bool inspectMode = false)
    {
        var arguments = new List<string>
        {
            "--request",
            requestFilePath,
            "--response",
            responseFilePath
        };
        if (inspectMode)
        {
            arguments.Add("--inspect");
        }

        return arguments;
    }
}
