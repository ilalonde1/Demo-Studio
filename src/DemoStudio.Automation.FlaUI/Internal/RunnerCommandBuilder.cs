namespace DemoStudio.Automation.FlaUI.Internal;

internal static class RunnerCommandBuilder
{
    internal static string BuildArguments(string requestFilePath, string responseFilePath, bool inspectMode = false)
    {
        var inspectArg = inspectMode ? " --inspect" : string.Empty;
        return $"--request {Quote(requestFilePath)} --response {Quote(responseFilePath)}{inspectArg}";
    }

    internal static string Quote(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        if (!argument.Contains('"') && !HasWhitespace(argument))
        {
            return argument;
        }

        var result = new System.Text.StringBuilder();
        result.Append('"');
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                result.Append('\\', backslashes * 2 + 1);
                result.Append('"');
                backslashes = 0;
                continue;
            }

            if (backslashes > 0)
            {
                result.Append('\\', backslashes);
                backslashes = 0;
            }

            result.Append(c);
        }

        if (backslashes > 0)
        {
            result.Append('\\', backslashes * 2);
        }

        result.Append('"');
        return result.ToString();
    }

    private static bool HasWhitespace(string value)
    {
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                return true;
            }
        }

        return false;
    }
}
