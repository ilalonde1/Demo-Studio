namespace DemoStudio.Application.Services;

using DemoStudio.Automation.Abstractions.Interfaces;
using DemoStudio.Domain.Enums;

public interface IAutomationEngineResolver
{
    IAutomationEngine Resolve(ApplicationType applicationType);
}
