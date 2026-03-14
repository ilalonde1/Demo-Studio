using System.Text.Json;
using System.IO;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopLaunchProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _profilesPath;
    private readonly SemaphoreSlim _sync = new(1, 1);

    public string? LastLoadDiagnostic { get; private set; }

    public DesktopLaunchProfileService(string storageRoot)
    {
        if (string.IsNullOrWhiteSpace(storageRoot))
        {
            throw new ArgumentException("Storage root is required.", nameof(storageRoot));
        }

        Directory.CreateDirectory(storageRoot);
        _profilesPath = Path.Combine(storageRoot, "launch-profiles.json");
    }

    public async Task<IReadOnlyList<DesktopLaunchProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task SaveAsync(DesktopLaunchProfile profile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException("Profile name is required.");
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profiles = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var existing = profiles.FindIndex(x => x.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                profiles[existing] = profile;
            }
            else
            {
                profiles.Add(profile);
            }

            await PersistUnsafeAsync(profiles, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task DeleteAsync(string profileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return;
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profiles = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            profiles.RemoveAll(x => x.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
            await PersistUnsafeAsync(profiles, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task<List<DesktopLaunchProfile>> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        var load = await DesktopAtomicJsonFile.LoadAsync<List<DesktopLaunchProfile>>(_profilesPath, JsonOptions, cancellationToken).ConfigureAwait(false);
        LastLoadDiagnostic = load.Diagnostic;
        if (!load.Exists)
        {
            return new List<DesktopLaunchProfile>();
        }

        if (load.Value is not null)
        {
            return load.Value;
        }

        throw new InvalidOperationException(LastLoadDiagnostic ?? "Launch profiles could not be loaded.");
    }

    private async Task PersistUnsafeAsync(List<DesktopLaunchProfile> profiles, CancellationToken cancellationToken)
    {
        await DesktopAtomicJsonFile.SaveAsync(_profilesPath, profiles, JsonOptions, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record DesktopLaunchProfile(
    string Name,
    string ExecutablePath,
    string? Arguments,
    string? WorkingDirectory,
    int StartupDelaySeconds,
    string? ExpectedWindowTitleContains,
    string? ExpectedProcessName);
