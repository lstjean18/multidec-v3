using System;
using System.Windows.Input;

namespace MultiDEC.App.ViewModels;

/// <summary>Commande WPF simple (pas de dépendance à un framework MVVM).</summary>
public sealed class Commande : ICommand
{
    private readonly Action _executer;
    private readonly Func<bool>? _possible;

    public Commande(Action executer, Func<bool>? possible = null)
    {
        _executer = executer;
        _possible = possible;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _possible?.Invoke() ?? true;

    public void Execute(object? parameter) => _executer();

    public void Rafraichir() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
