namespace DemoStudio.Application.Services;

using DemoStudio.Application.DTOs;
using DemoStudio.Application.Requests;

public interface IDemoFlowService
{
    Task<DemoFlowDto> CreateFromProposalAsync(CreateDemoFlowFromProposalCommand command, CancellationToken cancellationToken = default);

    Task<DemoStudio.Domain.Entities.DemoFlow?> GetEntityAsync(Guid flowId, CancellationToken cancellationToken = default);
}
