using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed class ClipCurationViewModel : INotifyPropertyChanged
{
    private string _nextClipLabel = string.Empty;
    private CurrentSessionClipItem? _selectedCurrentSessionClip;
    private bool _isClipCurationExpanded;
    private string? _activeClipLabel;
    private CurrentSessionClipItem? _activeClipItem;
    private DateTimeOffset _activeClipStartedUtc;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<CurrentSessionClipItem> CurrentSessionClips { get; } = new();

    public string NextClipLabel
    {
        get => _nextClipLabel;
        set
        {
            var normalized = value ?? string.Empty;
            if (_nextClipLabel == normalized)
            {
                return;
            }

            _nextClipLabel = normalized;
            OnPropertyChanged();
        }
    }

    public CurrentSessionClipItem? SelectedCurrentSessionClip
    {
        get => _selectedCurrentSessionClip;
        set
        {
            if (Equals(value, _selectedCurrentSessionClip))
            {
                return;
            }

            _selectedCurrentSessionClip = value;
            OnPropertyChanged();
        }
    }

    public bool IsClipCurationExpanded
    {
        get => _isClipCurationExpanded;
        set
        {
            if (value == _isClipCurationExpanded)
            {
                return;
            }

            _isClipCurationExpanded = value;
            OnPropertyChanged();
        }
    }

    public string? ActiveClipLabel
    {
        get => _activeClipLabel;
        set
        {
            if (_activeClipLabel == value)
            {
                return;
            }

            _activeClipLabel = value;
            OnPropertyChanged();
        }
    }

    public CurrentSessionClipItem? ActiveClipItem
    {
        get => _activeClipItem;
        set
        {
            if (Equals(value, _activeClipItem))
            {
                return;
            }

            _activeClipItem = value;
            OnPropertyChanged();
        }
    }

    public DateTimeOffset ActiveClipStartedUtc
    {
        get => _activeClipStartedUtc;
        set
        {
            if (_activeClipStartedUtc == value)
            {
                return;
            }

            _activeClipStartedUtc = value;
            OnPropertyChanged();
        }
    }

    public void ResetSessionState()
    {
        CurrentSessionClips.Clear();
        SelectedCurrentSessionClip = null;
        IsClipCurationExpanded = false;
        NextClipLabel = string.Empty;
        ActiveClipLabel = null;
        ActiveClipItem = null;
        ActiveClipStartedUtc = default;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
