namespace DemoStudio.Infrastructure.Execution;

using DemoStudio.Application.Services;
using DemoStudio.Domain.Entities;

public sealed class StubDesktopInspectionService : IDesktopInspectionService
{
    public Task<ElementInspectionResult> InspectAsync(ApplicationTarget target, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ElementInspectionResult(
            false,
            "Desktop inspection is unavailable because DesktopEngine is not configured to FlaUI.",
            null,
            Array.Empty<string>()));
    }
}
