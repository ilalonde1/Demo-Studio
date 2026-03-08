using DemoStudio.Application.Abstractions.Persistence;
using DemoStudio.Application.DTOs;
using DemoStudio.Application.Requests;
using DemoStudio.Application.Services;
using DemoStudio.Application.Validation;
using DemoStudio.Domain.Entities;
using DemoStudio.Domain.Enums;

namespace DemoStudio.Desktop.App.Tests;

public sealed class ApplicationConcurrencyHandlingTests
{
    [Fact]
    public async Task DemoProjectService_CreateAsync_ThrowsInvalidOperation_WhenConcurrencyConflict()
    {
        var service = new DemoProjectService(
            new FakeProjectRepository(),
            new ThrowingUnitOfWork(),
            new PassThroughValidator<CreateDemoProjectCommand>());
        var command = new CreateDemoProjectCommand("Project A", "PROJA", "desc");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(command));

        Assert.IsType<ConcurrencyConflictException>(ex.InnerException);
        Assert.Contains("could not be saved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DemoFlowService_CreateFromProposalAsync_ThrowsInvalidOperation_WhenConcurrencyConflict()
    {
        var project = new DemoProject("Project A", new Domain.ValueObjects.ProjectCode("PROJA"), null);
        var service = new DemoFlowService(
            new FakeProjectRepository { ExistingProject = project },
            new FakeFlowRepository(),
            new ThrowingUnitOfWork());
        var command = new CreateDemoFlowFromProposalCommand(
            project.Id,
            "Flow A",
            new[]
            {
                new CreateDemoFlowStepCommand(1, FlowStepType.Navigate, "open", null, 30)
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateFromProposalAsync(command));

        Assert.IsType<ConcurrencyConflictException>(ex.InnerException);
        Assert.Contains("could not be saved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DemoExecutionService_QueueRunAsync_ThrowsInvalidOperation_WhenConcurrencyConflict()
    {
        var service = new DemoExecutionService(
            new FakeRunRepository(),
            new ThrowingUnitOfWork(),
            new PassThroughValidator<QueueDemoRunCommand>(),
            new FakeRunPipeline());
        var command = new QueueDemoRunCommand(Guid.NewGuid(), Guid.NewGuid(), "tester");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueRunAsync(command));

        Assert.IsType<ConcurrencyConflictException>(ex.InnerException);
        Assert.Contains("concurrency conflict", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            throw new ConcurrencyConflictException("synthetic conflict");
        }
    }

    private sealed class PassThroughValidator<TCommand> : ICommandValidator<TCommand>
    {
        public ValidationResult Validate(TCommand command) => ValidationResult.Success();
    }

    private sealed class FakeProjectRepository : IDemoProjectRepository
    {
        public DemoProject? ExistingProject { get; init; }

        public Task AddAsync(DemoProject project, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<DemoProject?> GetByIdAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingProject);
        }

        public Task<DemoProject?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<DemoProject?>(null);
        }

        public Task<ApplicationTarget?> GetPrimaryTargetAsync(Guid demoProjectId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ApplicationTarget?>(null);
        }

        public Task<IReadOnlyCollection<DemoProject>> ListAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyCollection<DemoProject> items = Array.Empty<DemoProject>();
            return Task.FromResult(items);
        }
    }

    private sealed class FakeFlowRepository : IDemoFlowRepository
    {
        public Task AddAsync(DemoFlow flow, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<DemoFlow?> GetByIdWithStepsAsync(Guid flowId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<DemoFlow?>(null);
        }

        public Task<int> GetNextVersionAsync(Guid demoProjectId, string flowName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }
    }

    private sealed class FakeRunRepository : IDemoRunRepository
    {
        public Task AddAsync(DemoRun run, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<DemoRun?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<DemoRun?>(null);
        }

        public Task<DemoRunExecutionContext?> GetExecutionContextAsync(Guid runId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<DemoRunExecutionContext?>(null);
        }

        public Task<IReadOnlyCollection<DemoRun>> ListRecentAsync(int take, CancellationToken cancellationToken = default)
        {
            IReadOnlyCollection<DemoRun> items = Array.Empty<DemoRun>();
            return Task.FromResult(items);
        }

        public Task<DemoRun?> GetNextQueuedAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<DemoRun?>(null);
        }

        public Task<DemoRun?> ClaimNextQueuedAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<DemoRun?>(null);
        }
    }

    private sealed class FakeRunPipeline : IDemoRunPipeline
    {
        public Task<DemoRunPipelineResult> ExecuteAsync(DemoRunExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DemoRunPipelineResult(
                Succeeded: true,
                OutputDirectory: "out",
                RawVideoPath: "raw.mp4",
                RedactedVideoPath: null,
                LogPath: null,
                FailureReason: null));
        }
    }
}
