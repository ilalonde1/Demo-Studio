namespace DemoStudio.Application.Requests;

using DemoStudio.Domain.Enums;

public sealed record CreateDemoProjectCommand(string Name, string Code, string? Description);

public sealed record QueueDemoRunCommand(Guid DemoProjectId, Guid DemoFlowId, string RequestedBy);

public sealed record CreateDemoFlowFromProposalCommand(
    Guid DemoProjectId,
    string Name,
    IReadOnlyCollection<CreateDemoFlowStepCommand> Steps);

public sealed record CreateDemoFlowStepCommand(
    int Sequence,
    FlowStepType StepType,
    string ActionKey,
    string? PayloadJson,
    int TimeoutSeconds);
