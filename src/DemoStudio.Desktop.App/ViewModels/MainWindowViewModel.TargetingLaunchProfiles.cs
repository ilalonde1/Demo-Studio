using System.Diagnostics;
using DemoStudio.Desktop.App.Services;
using System.Windows;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task RefreshLaunchProfilesAsync()
    {
        if (!CanEditTargetSettings)
        {
            return;
        }

        var profiles = await _targetingUseCase.ListLaunchProfilesAsync();
        _targeting.ReplaceLaunchProfiles(profiles.Profiles.ToList());
        SelectedLaunchProfile = LaunchProfiles.FirstOrDefault();
        LaunchStatus = LaunchProfiles.Count == 0
            ? "No saved launch profiles."
            : $"Loaded {LaunchProfiles.Count} launch profiles.";
        if (!string.IsNullOrWhiteSpace(profiles.LoadDiagnostic))
        {
            LaunchStatus += $" Warning: {profiles.LoadDiagnostic}";
        }
        OnPropertyChanged(nameof(LaunchProfiles));
        OnPropertyChanged(nameof(LaunchStatus));
        RaiseCommandState();
    }

    private async Task SaveLaunchProfileAsync()
    {
        if (!CanSaveLaunchProfile)
        {
            return;
        }

        var profile = BuildLaunchProfileFromFields();
        await _targetingUseCase.SaveLaunchProfileAsync(profile);
        await RefreshLaunchProfilesAsync();
        SelectedLaunchProfile = LaunchProfiles.FirstOrDefault(x => x.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
        LaunchStatus = $"Saved launch profile '{profile.Name}'.";
        OnPropertyChanged(nameof(LaunchStatus));
    }

    private async Task DeleteSelectedLaunchProfileAsync()
    {
        if (!CanDeleteLaunchProfile || SelectedLaunchProfile is null)
        {
            return;
        }

        var name = SelectedLaunchProfile.Name;
        var confirm = MessageBox.Show(
            $"Delete launch profile '{name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await _targetingUseCase.DeleteLaunchProfileAsync(name);
        await RefreshLaunchProfilesAsync();
        LaunchStatus = $"Deleted launch profile '{name}'.";
        OnPropertyChanged(nameof(LaunchStatus));
    }

    private void LoadSelectedLaunchProfile()
    {
        if (!CanLoadLaunchProfile || SelectedLaunchProfile is null)
        {
            return;
        }

        LaunchProfileName = SelectedLaunchProfile.Name;
        LaunchExecutablePath = SelectedLaunchProfile.ExecutablePath;
        LaunchArguments = SelectedLaunchProfile.Arguments ?? string.Empty;
        LaunchWorkingDirectory = SelectedLaunchProfile.WorkingDirectory ?? string.Empty;
        LaunchStartupDelaySeconds = SelectedLaunchProfile.StartupDelaySeconds;

        if (!string.IsNullOrWhiteSpace(SelectedLaunchProfile.ExpectedWindowTitleContains))
        {
            WindowTitleContains = SelectedLaunchProfile.ExpectedWindowTitleContains;
        }

        if (!string.IsNullOrWhiteSpace(SelectedLaunchProfile.ExpectedProcessName))
        {
            WindowProcessName = SelectedLaunchProfile.ExpectedProcessName;
        }

        LaunchStatus = $"Loaded launch profile '{SelectedLaunchProfile.Name}'.";
        RaiseTargetingAndLaunchState();
        _ = RunPreflightAsync();
    }

    private async Task LaunchTargetAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        if (!CanLaunchTarget)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _targetingUseCase.LaunchAsync(BuildLaunchProfileFromFields());
            LaunchStatus = result.Message;
            _lastRuntimeMessage = result.Succeeded ? "Target launched. Refresh window picker and lock target." : result.Message;
            OnPropertyChanged(nameof(LaunchStatus));
            OnPropertyChanged(nameof(LastRuntimeMessage));
            await RunPreflightAsync();
        }
        catch (Exception ex)
        {
            LaunchStatus = $"Launch failed: {ex.Message}";
            _lastRuntimeMessage = BuildFailureDisplay("DS-DESK-LAUNCH-001", "Launch failed.", ex.Message);
            OnPropertyChanged(nameof(LaunchStatus));
            OnPropertyChanged(nameof(LastRuntimeMessage));
        }
        finally
        {
            SetBusy(false);
            RecordOperationMetric("LaunchTarget", stopwatch.Elapsed);
        }
    }

    private DesktopLaunchProfile BuildLaunchProfileFromFields()
    {
        return new DesktopLaunchProfile(
            Name: string.IsNullOrWhiteSpace(LaunchProfileName) ? "Unnamed" : LaunchProfileName.Trim(),
            ExecutablePath: LaunchExecutablePath.Trim(),
            Arguments: string.IsNullOrWhiteSpace(LaunchArguments) ? null : LaunchArguments.Trim(),
            WorkingDirectory: string.IsNullOrWhiteSpace(LaunchWorkingDirectory) ? null : LaunchWorkingDirectory.Trim(),
            StartupDelaySeconds: LaunchStartupDelaySeconds,
            ExpectedWindowTitleContains: string.IsNullOrWhiteSpace(WindowTitleContains) ? null : WindowTitleContains.Trim(),
            ExpectedProcessName: string.IsNullOrWhiteSpace(WindowProcessName) ? null : WindowProcessName.Trim());
    }

    private async Task RunPreflightAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await _preflightChecksUseCase.RunAsync(
                new DesktopPreflightChecksRequest(
                    IsStageMode,
                    EnsureStageWorkspaceReadyAsync,
                    BuildAndRunPreflightAsync),
                updateReadinessTimestamp: true);
            PreflightStatus = result.StatusText;
            ReadinessLastChecked = result.ReadinessLastChecked ?? ReadinessLastChecked;
            _lastRuntimeMessage = result.RuntimeMessage;
            OnPropertyChanged(nameof(PreflightStatus));
            OnPropertyChanged(nameof(ReadinessLastChecked));
            OnPropertyChanged(nameof(LastRuntimeMessage));
        }
        finally
        {
            RecordOperationMetric("Preflight", stopwatch.Elapsed);
        }
    }

    private Task<DesktopPreflightReport> BuildAndRunPreflightAsync()
    {
        return _targetingUseCase.RunPreflightAsync(_captureRuntime, BuildTargetSettings(), BuildLaunchProfileFromFields());
    }
}
