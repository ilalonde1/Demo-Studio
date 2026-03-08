namespace DemoStudio.Application.Services;

using DemoStudio.Application.DTOs;
using DemoStudio.Application.Requests;

public interface IDemoExecutionService
{
    Task<DemoRunDto> QueueRunAsync(QueueDemoRunCommand command, CancellationToken cancellationToken = default);

    Task<DemoRunDto> ExecuteRunAsync(Guid demoRunId, CancellationToken cancellationToken = default);

    Task<DemoRunDto?> ProcessNextQueuedRunAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DemoRunDto>> ListRecentRunsAsync(int take = 50, CancellationToken cancellationToken = default);
}
