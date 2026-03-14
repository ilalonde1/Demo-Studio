using System.Text.Json;
using System.IO;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<DesktopLaunchProfileService> _logger;

    public string? LastLoadDiagnostic { get; private set; }

    public DesktopLaunchProfileService(string storageRoot, ILogger<DesktopLaunchProfileService> logger)
    {
        if (string.IsNullOrWhiteSpace(storageRoot))
        {
            throw new ArgumentException("Storage root is required.", nameof(storageRoot));
        }

        Directory.CreateDirectory(storageRoot);
        _profilesPath = Path.Combine(storageRoot, "launch-profiles.json");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        var validation = DesktopLaunchProfilePolicy.ValidateForPersistence(profile);
        if (!validation.IsValid || validation.Profile is null)
        {
            throw new InvalidOperationException(validation.Error ?? "Launch profile is invalid.");
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profiles = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var normalizedProfile = validation.Profile;
            var existing = profiles.FindIndex(x => x.Name.Equals(normalizedProfile.Name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                profiles[existing] = normalizedProfile;
            }
            else
            {
                profiles.Add(normalizedProfile);
            }

            await PersistUnsafeAsync(profiles, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Launch profile {ProfileName} saved for {ExecutablePath}.", normalizedProfile.Name, normalizedProfile.ExecutablePath);
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
            _logger.LogInformation("Launch profile {ProfileName} deleted.", profileName);
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
            var validProfiles = new List<DesktopLaunchProfile>(load.Value.Count);
            var invalidProfiles = new List<string>();
            foreach (var profile in load.Value)
            {
                var validation = DesktopLaunchProfilePolicy.ValidateForPersistence(profile);
                if (!validation.IsValid || validation.Profile is null)
                {
                    invalidProfiles.Add($"{profile.Name}: {validation.Error}");
                    continue;
                }

                validProfiles.Add(validation.Profile);
            }

            if (invalidProfiles.Count > 0)
            {
                var invalidDiagnostic = $"Ignored invalid launch profiles: {string.Join(" | ", invalidProfiles)}";
                LastLoadDiagnostic = string.IsNullOrWhiteSpace(LastLoadDiagnostic)
                    ? invalidDiagnostic
                    : $"{LastLoadDiagnostic} {invalidDiagnostic}";
                _logger.LogWarning(invalidDiagnostic);
            }

            if (!string.IsNullOrWhiteSpace(load.Diagnostic))
            {
                _logger.LogWarning("Launch profiles loaded with recovery diagnostic: {Diagnostic}", load.Diagnostic);
            }

            return validProfiles;
        }

        _logger.LogError("Launch profiles load failed: {Diagnostic}", LastLoadDiagnostic);
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
