namespace DemoStudio.Application.Abstractions.System;

public interface IFileStorage
{
    Task<string> SaveAsync(string relativePath, Stream content, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);
}
