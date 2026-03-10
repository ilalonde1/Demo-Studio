namespace DemoStudio.Application.Services;

using DemoStudio.Application.Abstractions.Persistence;
using DemoStudio.Application.DTOs;
using DemoStudio.Application.Requests;
using DemoStudio.Application.Validation;
using DemoStudio.Domain.Entities;
using DemoStudio.Domain.ValueObjects;

public sealed class DemoProjectService : ApplicationServiceBase, IDemoProjectService
{
    private readonly IDemoProjectRepository _projectRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICommandValidator<CreateDemoProjectCommand> _validator;

    public DemoProjectService(
        IDemoProjectRepository projectRepository,
        IUnitOfWork unitOfWork,
        ICommandValidator<CreateDemoProjectCommand> validator)
    {
        _projectRepository = projectRepository;
        _unitOfWork = unitOfWork;
        _validator = validator;
    }

    public async Task<DemoProjectDto> CreateAsync(CreateDemoProjectCommand command, CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(command);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join("; ", validation.Errors));
        }

        var existing = await _projectRepository.GetByCodeAsync(command.Code, cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationException($"Project code '{command.Code}' already exists.");
        }

        var project = new DemoProject(command.Name, new ProjectCode(command.Code), command.Description);
        await _projectRepository.AddAsync(project, cancellationToken);

        await ExecuteSaveAsync(() => _unitOfWork.SaveChangesAsync(cancellationToken), "project");

        return ToDto(project);
    }

    public async Task<IReadOnlyCollection<DemoProjectDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var projects = await _projectRepository.ListAsync(cancellationToken);
        return projects.Select(ToDto).ToArray();
    }

    public async Task<DemoProjectDto?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken);
        return project is null ? null : ToDto(project);
    }

    private static DemoProjectDto ToDto(DemoProject project)
    {
        return new DemoProjectDto(
            project.Id,
            project.Name,
            project.Code.Value,
            project.Description,
            project.IsActive,
            project.CreatedUtc,
            project.UpdatedUtc);
    }
}
