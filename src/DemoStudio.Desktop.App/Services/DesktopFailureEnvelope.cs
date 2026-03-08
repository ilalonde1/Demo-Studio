namespace DemoStudio.Desktop.App.Services;

public sealed record DesktopFailureEnvelope(
    string Code,
    string Summary,
    string? Detail,
    string? FixHint)
{
    public string ToDisplayText()
    {
        var baseText = $"[{Code}] {Summary}";
        if (!string.IsNullOrWhiteSpace(Detail))
        {
            baseText += $" Detail: {Detail}";
        }

        if (!string.IsNullOrWhiteSpace(FixHint))
        {
            baseText += $" Fix: {FixHint}";
        }

        return baseText;
    }
}
