using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed class SessionStateViewModel : INotifyPropertyChanged
{
    private Guid _lastFinalizedSessionId = Guid.Empty;
    private string _lastOutputPath = "-";
    private string? _lastFailureCode;
    private string? _lastDiagnosticsPath;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid LastFinalizedSessionId
    {
        get => _lastFinalizedSessionId;
        set => SetField(ref _lastFinalizedSessionId, value);
    }

    public string LastOutputPath
    {
        get => _lastOutputPath;
        set => SetField(ref _lastOutputPath, string.IsNullOrWhiteSpace(value) ? "-" : value);
    }

    public string? LastFailureCode
    {
        get => _lastFailureCode;
        set => SetField(ref _lastFailureCode, value);
    }

    public string? LastDiagnosticsPath
    {
        get => _lastDiagnosticsPath;
        set => SetField(ref _lastDiagnosticsPath, value);
    }

    public void ResetForSessionRestart()
    {
        LastFinalizedSessionId = Guid.Empty;
        LastOutputPath = "-";
        LastFailureCode = null;
        LastDiagnosticsPath = null;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
