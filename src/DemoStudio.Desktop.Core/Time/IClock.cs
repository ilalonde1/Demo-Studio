namespace DemoStudio.Desktop.Core.Time;
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
