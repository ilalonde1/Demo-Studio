using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DemoStudio.Desktop.App.Services;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed class SessionHistoryViewModel : INotifyPropertyChanged
{
    private DesktopSessionRecord? _selectedRecord;
    private string _status = "No session history loaded.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DesktopSessionRecord> Records { get; } = new();

    public DesktopSessionRecord? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (Equals(value, _selectedRecord))
            {
                return;
            }

            _selectedRecord = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (value == _status)
            {
                return;
            }

            _status = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
