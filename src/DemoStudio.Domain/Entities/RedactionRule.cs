namespace DemoStudio.Domain.Entities;

using DemoStudio.Domain.Common;

public sealed class RedactionRule : BaseEntity
{
    private RedactionRule() : base(Guid.NewGuid())
    {
    }

    public RedactionRule(Guid demoProjectId, string name, string matchExpression, string replacementText) : base(Guid.NewGuid())
    {
        if (demoProjectId == Guid.Empty)
        {
            throw new ArgumentException("Project identifier is required.", nameof(demoProjectId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Rule name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(matchExpression))
        {
            throw new ArgumentException("Match expression is required.", nameof(matchExpression));
        }

        if (string.IsNullOrWhiteSpace(replacementText))
        {
            throw new ArgumentException("Replacement text is required.", nameof(replacementText));
        }

        DemoProjectId = demoProjectId;
        Name = name.Trim();
        MatchExpression = matchExpression.Trim();
        ReplacementText = replacementText.Trim();
        IsEnabled = true;
    }

    public Guid DemoProjectId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string MatchExpression { get; private set; } = string.Empty;

    public string ReplacementText { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    public void SetEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
        MarkUpdated();
    }
}
