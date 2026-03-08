namespace DemoStudio.Domain.Entities;

using DemoStudio.Domain.Common;

public sealed class DemoFlow : BaseEntity
{
    private readonly List<FlowStep> _steps = new();

    private DemoFlow() : base(Guid.NewGuid())
    {
    }

    public DemoFlow(Guid demoProjectId, string name, int version) : base(Guid.NewGuid())
    {
        if (demoProjectId == Guid.Empty)
        {
            throw new ArgumentException("Demo project identifier is required.", nameof(demoProjectId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Flow name is required.", nameof(name));
        }

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Flow version must be greater than zero.");
        }

        DemoProjectId = demoProjectId;
        Name = name.Trim();
        Version = version;
        IsDeterministic = true;
    }

    public Guid DemoProjectId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int Version { get; private set; }

    public bool IsDeterministic { get; private set; }

    public IReadOnlyCollection<FlowStep> Steps => _steps;

    public void AddStep(FlowStep step)
    {
        if (step is null)
        {
            throw new ArgumentNullException(nameof(step));
        }

        if (step.DemoFlowId != Id)
        {
            throw new InvalidOperationException("Step flow identifier does not match flow.");
        }

        _steps.Add(step);
        MarkUpdated();
    }
}
