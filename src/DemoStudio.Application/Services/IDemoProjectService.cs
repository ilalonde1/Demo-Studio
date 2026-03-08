namespace DemoStudio.Application.Services;

using DemoStudio.Application.DTOs;
using DemoStudio.Application.Requests;

public interface IDemoProjectService
{
    Task<DemoProjectDto> CreateAsync(CreateDemoProjectCommand command, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DemoProjectDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<DemoProjectDto?> GetAsync(Guid projectId, CancellationToken cancellationToken = default);
}
