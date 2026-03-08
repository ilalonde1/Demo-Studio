namespace DemoStudio.Domain.Entities;

using DemoStudio.Domain.Common;
using DemoStudio.Domain.Enums;

public sealed class ApplicationTarget : BaseEntity
{
    private ApplicationTarget() : base(Guid.NewGuid())
    {
    }

    public ApplicationTarget(Guid demoProjectId, string name, ApplicationType applicationType, string targetReference) : base(Guid.NewGuid())
    {
        if (demoProjectId == Guid.Empty)
        {
            throw new ArgumentException("Demo project identifier is required.", nameof(demoProjectId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Target name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(targetReference))
        {
            throw new ArgumentException("Target reference is required.", nameof(targetReference));
        }

        DemoProjectId = demoProjectId;
        Name = name.Trim();
        ApplicationType = applicationType;
        TargetReference = targetReference.Trim();
    }

    public Guid DemoProjectId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public ApplicationType ApplicationType { get; private set; }

    public string TargetReference { get; private set; } = string.Empty;
}
