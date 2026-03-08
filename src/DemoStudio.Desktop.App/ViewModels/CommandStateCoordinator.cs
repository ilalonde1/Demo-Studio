using System.Windows.Input;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed class CommandStateCoordinator
{
    private readonly List<RelayCommand> _commands = new();

    public void Register(params ICommand[] commands)
    {
        _commands.Clear();
        foreach (var command in commands)
        {
            if (command is RelayCommand relay)
            {
                _commands.Add(relay);
            }
        }
    }

    public void RaiseCanExecuteChanged()
    {
        foreach (var command in _commands)
        {
            command.RaiseCanExecuteChanged();
        }
    }
}
