using DemoStudio.Application.Abstractions.Persistence;

namespace DemoStudio.Application.Services;

public abstract class ApplicationServiceBase
{
    protected static async Task ExecuteSaveAsync(Func<Task> saveOperation, string context)
    {
        try
        {
            await saveOperation();
        }
        catch (ConcurrencyConflictException ex)
        {
            throw new InvalidOperationException(
                $"The {context} could not be saved because related data changed during execution.", ex);
        }
    }
}
