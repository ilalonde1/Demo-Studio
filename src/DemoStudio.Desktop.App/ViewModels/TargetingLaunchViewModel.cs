using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DemoStudio.Desktop.App.Services;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed class TargetingLaunchViewModel : INotifyPropertyChanged
{
    private string _captureMode = "Window";
    private string _windowTitleContains = string.Empty;
    private string _windowProcessName = string.Empty;
    private string _windowHandleHex = string.Empty;
    private bool _fallbackToDesktop;
    private DesktopWindowCandidate? _selectedWindowCandidate;
    private string _windowSelectionStatus = "No window selected.";
    private string _launchProfileName = string.Empty;
    private string _launchExecutablePath = string.Empty;
    private string _launchArguments = string.Empty;
    private string _launchWorkingDirectory = string.Empty;
    private int _launchStartupDelaySeconds = 2;
    private DesktopLaunchProfile? _selectedLaunchProfile;
    private string _launchStatus = "No launch profile selected.";
    private string _preflightStatus = "Setup check not run yet.";
    private string _readinessLastChecked = "Not checked yet.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DesktopWindowCandidate> WindowCandidates { get; } = new();

    public string CaptureMode
    {
        get => _captureMode;
        set
        {
            var next = string.Equals(value, "Desktop", StringComparison.OrdinalIgnoreCase) ? "Desktop" : "Window";
            if (next == _captureMode)
            {
                return;
            }

            _captureMode = next;
            OnPropertyChanged();
        }
    }

    public string WindowTitleContains
    {
        get => _windowTitleContains;
        set
        {
            if (value == _windowTitleContains)
            {
                return;
            }

            _windowTitleContains = value;
            OnPropertyChanged();
        }
    }

    public string WindowProcessName
    {
        get => _windowProcessName;
        set
        {
            if (value == _windowProcessName)
            {
                return;
            }

            _windowProcessName = value;
            OnPropertyChanged();
        }
    }

    public string WindowHandleHex
    {
        get => _windowHandleHex;
        set
        {
            if (value == _windowHandleHex)
            {
                return;
            }

            _windowHandleHex = value;
            OnPropertyChanged();
        }
    }

    public bool FallbackToDesktop
    {
        get => _fallbackToDesktop;
        set
        {
            if (value == _fallbackToDesktop)
            {
                return;
            }

            _fallbackToDesktop = value;
            OnPropertyChanged();
        }
    }

    public DesktopWindowCandidate? SelectedWindowCandidate
    {
        get => _selectedWindowCandidate;
        set
        {
            if (Equals(value, _selectedWindowCandidate))
            {
                return;
            }

            _selectedWindowCandidate = value;
            OnPropertyChanged();
        }
    }

    public string WindowSelectionStatus
    {
        get => _windowSelectionStatus;
        set
        {
            if (value == _windowSelectionStatus)
            {
                return;
            }

            _windowSelectionStatus = value;
            OnPropertyChanged();
        }
    }

    public string LaunchProfileName
    {
        get => _launchProfileName;
        set
        {
            if (value == _launchProfileName)
            {
                return;
            }

            _launchProfileName = value;
            OnPropertyChanged();
        }
    }

    public string LaunchExecutablePath
    {
        get => _launchExecutablePath;
        set
        {
            if (value == _launchExecutablePath)
            {
                return;
            }

            _launchExecutablePath = value;
            OnPropertyChanged();
        }
    }

    public string LaunchArguments
    {
        get => _launchArguments;
        set
        {
            if (value == _launchArguments)
            {
                return;
            }

            _launchArguments = value;
            OnPropertyChanged();
        }
    }

    public string LaunchWorkingDirectory
    {
        get => _launchWorkingDirectory;
        set
        {
            if (value == _launchWorkingDirectory)
            {
                return;
            }

            _launchWorkingDirectory = value;
            OnPropertyChanged();
        }
    }

    public int LaunchStartupDelaySeconds
    {
        get => _launchStartupDelaySeconds;
        set
        {
            var normalized = Math.Clamp(value, 0, 30);
            if (normalized == _launchStartupDelaySeconds)
            {
                return;
            }

            _launchStartupDelaySeconds = normalized;
            OnPropertyChanged();
        }
    }

    public List<DesktopLaunchProfile> LaunchProfiles { get; private set; } = new();

    public DesktopLaunchProfile? SelectedLaunchProfile
    {
        get => _selectedLaunchProfile;
        set
        {
            if (Equals(value, _selectedLaunchProfile))
            {
                return;
            }

            _selectedLaunchProfile = value;
            OnPropertyChanged();
        }
    }

    public string LaunchStatus
    {
        get => _launchStatus;
        set
        {
            if (value == _launchStatus)
            {
                return;
            }

            _launchStatus = value;
            OnPropertyChanged();
        }
    }

    public string PreflightStatus
    {
        get => _preflightStatus;
        set
        {
            if (value == _preflightStatus)
            {
                return;
            }

            _preflightStatus = value;
            OnPropertyChanged();
        }
    }

    public string ReadinessLastChecked
    {
        get => _readinessLastChecked;
        set
        {
            if (value == _readinessLastChecked)
            {
                return;
            }

            _readinessLastChecked = value;
            OnPropertyChanged();
        }
    }

    public void ReplaceLaunchProfiles(List<DesktopLaunchProfile> profiles)
    {
        LaunchProfiles = profiles ?? new List<DesktopLaunchProfile>();
        OnPropertyChanged(nameof(LaunchProfiles));
    }

    public void InitializeFromDefaults(CaptureTargetSettings defaults)
    {
        CaptureMode = string.IsNullOrWhiteSpace(defaults.Mode) ? "Window" : defaults.Mode;
        WindowTitleContains = defaults.WindowTitleContains ?? string.Empty;
        WindowProcessName = defaults.WindowProcessName ?? string.Empty;
        WindowHandleHex = defaults.WindowHandleHex ?? string.Empty;
        FallbackToDesktop = defaults.FallbackToDesktop;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
