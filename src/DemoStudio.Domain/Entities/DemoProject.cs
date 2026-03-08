namespace DemoStudio.Domain.Entities;

using DemoStudio.Domain.Common;
using DemoStudio.Domain.ValueObjects;

public sealed class DemoProject : BaseEntity
{
    private readonly List<ApplicationTarget> _targets = new();
    private readonly List<DemoFlow> _flows = new();
    private readonly List<RedactionRule> _redactionRules = new();

    private DemoProject() : base(Guid.NewGuid())
    {
    }

    public DemoProject(string name, ProjectCode code, string? description) : base(Guid.NewGuid())
    {
        Rename(name);
        Code = code;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;

    public ProjectCode Code { get; private set; } = new("UNSET");

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<ApplicationTarget> Targets => _targets;

    public IReadOnlyCollection<DemoFlow> Flows => _flows;

    public IReadOnlyCollection<RedactionRule> RedactionRules => _redactionRules;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Project name is required.", nameof(name));
        }

        Name = name.Trim();
        MarkUpdated();
    }

    public void SetDescription(string? description)
    {
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        MarkUpdated();
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        MarkUpdated();
    }

    public void AddTarget(ApplicationTarget target)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (target.DemoProjectId != Id)
        {
            throw new InvalidOperationException("Target project identifier does not match project.");
        }

        _targets.Add(target);
        MarkUpdated();
    }

    public void AddFlow(DemoFlow flow)
    {
        if (flow is null)
        {
            throw new ArgumentNullException(nameof(flow));
        }

        if (flow.DemoProjectId != Id)
        {
            throw new InvalidOperationException("Flow project identifier does not match project.");
        }

        _flows.Add(flow);
        MarkUpdated();
    }

    public void AddRedactionRule(RedactionRule rule)
    {
        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        if (rule.DemoProjectId != Id)
        {
            throw new InvalidOperationException("Redaction rule project identifier does not match project.");
        }

        _redactionRules.Add(rule);
        MarkUpdated();
    }
}
