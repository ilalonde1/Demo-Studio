namespace DemoStudio.Application.Services;

using DemoStudio.Application.Abstractions.Persistence;
using DemoStudio.Application.DTOs;
using DemoStudio.Application.Requests;
using DemoStudio.Domain.Entities;

public sealed class DemoFlowService : IDemoFlowService
{
    private readonly IDemoProjectRepository _projectRepository;
    private readonly IDemoFlowRepository _flowRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DemoFlowService(
        IDemoProjectRepository projectRepository,
        IDemoFlowRepository flowRepository,
        IUnitOfWork unitOfWork)
    {
        _projectRepository = projectRepository;
        _flowRepository = flowRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<DemoFlowDto> CreateFromProposalAsync(CreateDemoFlowFromProposalCommand command, CancellationToken cancellationToken = default)
    {
        if (command.DemoProjectId == Guid.Empty)
        {
            throw new InvalidOperationException("Demo project is required.");
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            throw new InvalidOperationException("Flow name is required.");
        }

        if (command.Steps.Count == 0)
        {
            throw new InvalidOperationException("At least one flow step is required.");
        }

        var project = await _projectRepository.GetByIdAsync(command.DemoProjectId, cancellationToken);
        if (project is null)
        {
            throw new InvalidOperationException($"Project '{command.DemoProjectId}' was not found.");
        }

        var normalizedName = command.Name.Trim();
        var nextVersion = await _flowRepository.GetNextVersionAsync(command.DemoProjectId, normalizedName, cancellationToken);
        var flow = new DemoFlow(command.DemoProjectId, normalizedName, nextVersion);

        foreach (var stepCommand in command.Steps.OrderBy(x => x.Sequence))
        {
            var step = new FlowStep(
                flow.Id,
                stepCommand.Sequence,
                stepCommand.StepType,
                stepCommand.ActionKey,
                stepCommand.PayloadJson,
                stepCommand.TimeoutSeconds);
            flow.AddStep(step);
        }

        await _flowRepository.AddAsync(flow, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException ex)
        {
            throw new InvalidOperationException("The flow could not be saved because related data changed during execution.", ex);
        }

        return new DemoFlowDto(
            flow.Id,
            flow.DemoProjectId,
            flow.Name,
            flow.Version,
            flow.IsDeterministic,
            flow.Steps.Count);
    }

    public Task<DemoFlow?> GetEntityAsync(Guid flowId, CancellationToken cancellationToken = default)
    {
        if (flowId == Guid.Empty)
        {
            throw new InvalidOperationException("Flow identifier is required.");
        }

        return _flowRepository.GetByIdWithStepsAsync(flowId, cancellationToken);
    }
}
