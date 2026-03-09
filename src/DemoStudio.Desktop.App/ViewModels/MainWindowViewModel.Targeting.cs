using System.Diagnostics;
using DemoStudio.Desktop.App.Services;
using System.Windows;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void EnsureWindowTargetLockedFromSelection()
    {
        if (!string.Equals(CaptureMode, "Window", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (SelectedWindowCandidate is null)
        {
            return;
        }

        var selected = SelectedWindowCandidate;
        WindowTitleContains = selected.Title ?? string.Empty;
        WindowProcessName = selected.ProcessName ?? string.Empty;
        WindowHandleHex = selected.HandleHex ?? string.Empty;
        WindowSelectionStatus = $"Target locked to {selected.DisplayName}.";
        OnPropertyChanged(nameof(WindowSelectionStatus));
    }

    private void RefreshWindowCandidates()
    {
        if (!CanEditTargetSettings)
        {
            return;
        }

        var priorHandle = SelectedWindowCandidate?.HandleHex;
        var lockedHandle = string.IsNullOrWhiteSpace(WindowHandleHex) ? null : WindowHandleHex;

        var windows = _windowCatalogService.ListCapturableWindows();
        WindowCandidates.Clear();
        foreach (var window in windows)
        {
            WindowCandidates.Add(window);
        }

        SelectedWindowCandidate =
            (!string.IsNullOrWhiteSpace(priorHandle)
                ? WindowCandidates.FirstOrDefault(x => string.Equals(x.HandleHex, priorHandle, StringComparison.OrdinalIgnoreCase))
                : null)
            ?? (!string.IsNullOrWhiteSpace(lockedHandle)
                ? WindowCandidates.FirstOrDefault(x => string.Equals(x.HandleHex, lockedHandle, StringComparison.OrdinalIgnoreCase))
                : null)
            ?? WindowCandidates.FirstOrDefault();
        WindowSelectionStatus = windows.Count == 0
            ? "No capturable windows found."
            : SelectedWindowCandidate is null
                ? $"Found {windows.Count} capturable windows."
                : $"Found {windows.Count} capturable windows. Selected: {SelectedWindowCandidate.DisplayName}.";
        OnPropertyChanged(nameof(WindowSelectionStatus));
        RaiseCommandState();
    }

    private async Task UseSelectedWindowAsync()
    {
        if (!CanUseSelectedWindow || SelectedWindowCandidate is null)
        {
            return;
        }

        CaptureMode = "Window";
        WindowTitleContains = SelectedWindowCandidate.Title;
        WindowProcessName = SelectedWindowCandidate.ProcessName;
        WindowHandleHex = SelectedWindowCandidate.HandleHex;
        WindowSelectionStatus = $"Target locked to {SelectedWindowCandidate.DisplayName}.";
        RaiseTargetingAndLaunchState();

        try
        {
            await RunPreflightAsync();
        }
        catch (Exception ex)
        {
            SetRuntimeFailure("DS-DESK-PREF-001", "Target readiness check failed.", ex);
        }
    }

    private async Task FocusTargetAsync()
    {
        if (!CanFocusTarget)
        {
            return;
        }

        try
        {
            EnsureWindowTargetLockedFromSelection();
            var result = await _windowFocusService.TryActivateAsync(BuildTargetSettings());
            _lastRuntimeMessage = result.Message;
            OnPropertyChanged(nameof(LastRuntimeMessage));
        }
        catch (Exception ex)
        {
            SetRuntimeFailure("DS-DESK-FOCUS-001", "Focus target failed.", ex);
        }
    }

}
