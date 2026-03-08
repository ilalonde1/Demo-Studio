namespace DemoStudio.Application.Abstractions.Persistence;

using DemoStudio.Domain.Entities;

public sealed record DemoRunExecutionContext(
    DemoRun Run,
    DemoProject Project,
    ApplicationTarget Target,
    DemoFlow Flow,
    IReadOnlyCollection<FlowStep> Steps,
    IReadOnlyCollection<RedactionRule> RedactionRules);
