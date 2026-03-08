namespace DemoStudio.Domain.ValueObjects;

public sealed record ProjectCode
{
    public ProjectCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Project code is required.", nameof(value));
        }

        var trimmed = value.Trim().ToUpperInvariant();
        if (trimmed.Length > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Project code cannot exceed 32 characters.");
        }

        Value = trimmed;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
