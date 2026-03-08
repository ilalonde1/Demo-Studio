using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DemoStudio.Desktop.App.Services;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed class OnboardingViewModel : INotifyPropertyChanged
{
    private readonly DesktopOnboardingService _service;
    private int _stepIndex;
    private bool _isVisible;

    public OnboardingViewModel(DesktopOnboardingService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        PreviousCommand = new RelayCommand(_ => MovePrevious(), _ => CanMovePrevious);
        NextCommand = new RelayCommand(_ => MoveNext(), _ => CanMoveNext);
        DismissCommand = new RelayCommand(_ => Dismiss());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsVisible => _isVisible;

    public int StepNumber => _stepIndex + 1;

    public string Title => _stepIndex switch
    {
        0 => "Step 1: Pick the right target",
        1 => "Step 2: Record in clean clip segments",
        _ => "Step 3: Curate, compose, publish"
    };

    public string Detail => _stepIndex switch
    {
        0 => "Choose Record Type and Target Window.",
        1 => "Click Start Recording. Use Pause/Resume to create clear segment boundaries.",
        _ => "Use Open Clip Editor to reorder clips, Build Final Video, then create your package."
    };

    public string NextLabel => _stepIndex >= 2 ? "Finish" : "Next";

    public bool CanMovePrevious => _isVisible && _stepIndex > 0;

    public bool CanMoveNext => _isVisible;

    public ICommand PreviousCommand { get; }

    public ICommand NextCommand { get; }

    public ICommand DismissCommand { get; }

    public void Initialize()
    {
        _isVisible = _service.ShouldShow();
        RaiseAll();
    }

    public void MoveNext()
    {
        if (!_isVisible)
        {
            return;
        }

        if (_stepIndex >= 2)
        {
            Dismiss();
            return;
        }

        _stepIndex++;
        RaiseAll();
    }

    public void MovePrevious()
    {
        if (!_isVisible || _stepIndex == 0)
        {
            return;
        }

        _stepIndex--;
        RaiseAll();
    }

    public void Dismiss()
    {
        _isVisible = false;
        _service.MarkComplete();
        RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(StepNumber));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(NextLabel));
        OnPropertyChanged(nameof(CanMovePrevious));
        OnPropertyChanged(nameof(CanMoveNext));
        if (PreviousCommand is RelayCommand previous)
        {
            previous.RaiseCanExecuteChanged();
        }

        if (NextCommand is RelayCommand next)
        {
            next.RaiseCanExecuteChanged();
        }

        if (DismissCommand is RelayCommand dismiss)
        {
            dismiss.RaiseCanExecuteChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
