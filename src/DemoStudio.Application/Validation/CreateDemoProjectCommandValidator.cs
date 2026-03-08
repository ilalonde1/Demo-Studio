namespace DemoStudio.Application.Validation;

using DemoStudio.Application.Requests;

public sealed class CreateDemoProjectCommandValidator : ICommandValidator<CreateDemoProjectCommand>
{
    public ValidationResult Validate(CreateDemoProjectCommand command)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            errors.Add("Project name is required.");
        }

        if (string.IsNullOrWhiteSpace(command.Code))
        {
            errors.Add("Project code is required.");
        }

        return errors.Count == 0 ? ValidationResult.Success() : new ValidationResult(false, errors);
    }
}
