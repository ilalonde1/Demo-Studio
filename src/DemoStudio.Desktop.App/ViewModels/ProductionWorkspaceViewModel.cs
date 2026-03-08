using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DemoStudio.Desktop.App.Services;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed class ProductionWorkspaceViewModel : INotifyPropertyChanged
{
    private bool _captureNarration;
    private string _microphoneDeviceName = string.Empty;
    private bool _presenterViewEnabled;
    private string _presenterNotes = string.Empty;
    private string _templateName = "Portfolio Demo";
    private string _selectedTemplateName = string.Empty;
    private string _selectedComposeQualityPreset = "Balanced";
    private string _selectedExportStyle = "Portfolio Clean";
    private string _composeStatus = "Final video not built yet.";
    private string _publishStatus = "Publish package not generated.";
    private string _shareSummary = "Share summary not generated yet.";
    private string _lastPublishPackagePath = string.Empty;
    private string _aiNarrationProvider = "OpenAI";
    private string _aiNarrationBaseUrl = string.Empty;
    private string _aiNarrationModel = "gpt-4o-mini-tts";
    private string _aiNarrationVoice = "alloy";
    private string _aiNarrationApiKey = string.Empty;
    private bool _aiAutoTrimScript = true;
    private double _aiWordsPerSecond = 2.6d;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DesktopDemoTemplate> DemoTemplates { get; } = new();
    public IReadOnlyList<string> ComposeQualityPresets { get; } = new[] { "Fast", "Balanced", "Portfolio" };
    public IReadOnlyList<string> ExportStylePresets { get; } = new[] { "Portfolio Clean", "Tutorial", "Social Reel" };

    public bool CaptureNarration
    {
        get => _captureNarration;
        set
        {
            if (value == _captureNarration)
            {
                return;
            }

            _captureNarration = value;
            OnPropertyChanged();
        }
    }

    public string MicrophoneDeviceName
    {
        get => _microphoneDeviceName;
        set
        {
            var normalized = value ?? string.Empty;
            if (_microphoneDeviceName == normalized)
            {
                return;
            }

            _microphoneDeviceName = normalized;
            OnPropertyChanged();
        }
    }

    public bool PresenterViewEnabled
    {
        get => _presenterViewEnabled;
        set
        {
            if (value == _presenterViewEnabled)
            {
                return;
            }

            _presenterViewEnabled = value;
            OnPropertyChanged();
        }
    }

    public string PresenterNotes
    {
        get => _presenterNotes;
        set
        {
            var normalized = value ?? string.Empty;
            if (_presenterNotes == normalized)
            {
                return;
            }

            _presenterNotes = normalized;
            OnPropertyChanged();
        }
    }

    public string TemplateName
    {
        get => _templateName;
        set
        {
            var normalized = value ?? string.Empty;
            if (_templateName == normalized)
            {
                return;
            }

            _templateName = normalized;
            OnPropertyChanged();
        }
    }

    public string SelectedTemplateName
    {
        get => _selectedTemplateName;
        set
        {
            var normalized = value ?? string.Empty;
            if (_selectedTemplateName == normalized)
            {
                return;
            }

            _selectedTemplateName = normalized;
            OnPropertyChanged();
        }
    }

    public string SelectedComposeQualityPreset
    {
        get => _selectedComposeQualityPreset;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "Balanced" : value.Trim();
            if (!ComposeQualityPresets.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                normalized = "Balanced";
            }

            if (string.Equals(_selectedComposeQualityPreset, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _selectedComposeQualityPreset = normalized;
            OnPropertyChanged();
        }
    }

    public string SelectedExportStyle
    {
        get => _selectedExportStyle;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "Portfolio Clean" : value.Trim();
            if (!ExportStylePresets.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                normalized = "Portfolio Clean";
            }

            if (string.Equals(_selectedExportStyle, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _selectedExportStyle = normalized;
            OnPropertyChanged();
        }
    }

    public string ComposeStatus
    {
        get => _composeStatus;
        set
        {
            var normalized = value ?? string.Empty;
            if (_composeStatus == normalized)
            {
                return;
            }

            _composeStatus = normalized;
            OnPropertyChanged();
        }
    }

    public string PublishStatus
    {
        get => _publishStatus;
        set
        {
            var normalized = value ?? string.Empty;
            if (_publishStatus == normalized)
            {
                return;
            }

            _publishStatus = normalized;
            OnPropertyChanged();
        }
    }

    public string ShareSummary
    {
        get => _shareSummary;
        set
        {
            var normalized = value ?? string.Empty;
            if (_shareSummary == normalized)
            {
                return;
            }

            _shareSummary = normalized;
            OnPropertyChanged();
        }
    }

    public string LastPublishPackagePath
    {
        get => _lastPublishPackagePath;
        set
        {
            var normalized = value ?? string.Empty;
            if (_lastPublishPackagePath == normalized)
            {
                return;
            }

            _lastPublishPackagePath = normalized;
            OnPropertyChanged();
        }
    }

    public string AiNarrationProvider
    {
        get => _aiNarrationProvider;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "OpenAI" : value.Trim();
            if (string.Equals(_aiNarrationProvider, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _aiNarrationProvider = normalized;
            OnPropertyChanged();
        }
    }

    public string AiNarrationBaseUrl
    {
        get => _aiNarrationBaseUrl;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_aiNarrationBaseUrl, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _aiNarrationBaseUrl = normalized;
            OnPropertyChanged();
        }
    }

    public string AiNarrationModel
    {
        get => _aiNarrationModel;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "gpt-4o-mini-tts" : value.Trim();
            if (string.Equals(_aiNarrationModel, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _aiNarrationModel = normalized;
            OnPropertyChanged();
        }
    }

    public string AiNarrationVoice
    {
        get => _aiNarrationVoice;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "alloy" : value.Trim();
            if (string.Equals(_aiNarrationVoice, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _aiNarrationVoice = normalized;
            OnPropertyChanged();
        }
    }

    public string AiNarrationApiKey
    {
        get => _aiNarrationApiKey;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_aiNarrationApiKey, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _aiNarrationApiKey = normalized;
            OnPropertyChanged();
        }
    }

    public bool AiAutoTrimScript
    {
        get => _aiAutoTrimScript;
        set
        {
            if (value == _aiAutoTrimScript)
            {
                return;
            }

            _aiAutoTrimScript = value;
            OnPropertyChanged();
        }
    }

    public double AiWordsPerSecond
    {
        get => _aiWordsPerSecond;
        set
        {
            var normalized = Math.Clamp(value, 1.2d, 4.5d);
            if (Math.Abs(_aiWordsPerSecond - normalized) < 0.001d)
            {
                return;
            }

            _aiWordsPerSecond = normalized;
            OnPropertyChanged();
        }
    }

    public void InitializeMicrophoneSettings(bool captureNarration, string microphoneDeviceName)
    {
        CaptureNarration = captureNarration;
        MicrophoneDeviceName = microphoneDeviceName;
    }

    public void ResetSessionOutputs()
    {
        ComposeStatus = "Final video not built yet.";
        PublishStatus = "Publish package not generated.";
        ShareSummary = "Share summary not generated yet.";
        LastPublishPackagePath = string.Empty;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
