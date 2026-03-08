namespace DemoStudio.Domain.Entities;

using DemoStudio.Domain.Common;
using DemoStudio.Domain.Enums;

public sealed class FlowStep : BaseEntity
{
    private FlowStep() : base(Guid.NewGuid())
    {
    }

    public FlowStep(Guid demoFlowId, int sequence, FlowStepType stepType, string actionKey, string? payloadJson, int timeoutSeconds) : base(Guid.NewGuid())
    {
        if (demoFlowId == Guid.Empty)
        {
            throw new ArgumentException("Flow identifier is required.", nameof(demoFlowId));
        }

        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(actionKey))
        {
            throw new ArgumentException("Action key is required.", nameof(actionKey));
        }

        if (timeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Timeout must be greater than zero.");
        }

        DemoFlowId = demoFlowId;
        Sequence = sequence;
        StepType = stepType;
        ActionKey = actionKey.Trim();
        PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? null : payloadJson.Trim();
        TimeoutSeconds = timeoutSeconds;
    }

    public Guid DemoFlowId { get; private set; }

    public int Sequence { get; private set; }

    public FlowStepType StepType { get; private set; }

    public string ActionKey { get; private set; } = string.Empty;

    public string? PayloadJson { get; private set; }

    public int TimeoutSeconds { get; private set; }
}
