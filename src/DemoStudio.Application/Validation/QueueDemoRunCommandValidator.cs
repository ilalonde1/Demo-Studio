namespace DemoStudio.Application.Validation;

using DemoStudio.Application.Requests;

public sealed class QueueDemoRunCommandValidator : ICommandValidator<QueueDemoRunCommand>
{
    public ValidationResult Validate(QueueDemoRunCommand command)
    {
        var errors = new List<string>();

        if (command.DemoProjectId == Guid.Empty)
        {
            errors.Add("DemoProjectId is required.");
        }

        if (command.DemoFlowId == Guid.Empty)
        {
            errors.Add("DemoFlowId is required.");
        }

        if (string.IsNullOrWhiteSpace(command.RequestedBy))
        {
            errors.Add("RequestedBy is required.");
        }

        return errors.Count == 0 ? ValidationResult.Success() : new ValidationResult(false, errors);
    }
}
