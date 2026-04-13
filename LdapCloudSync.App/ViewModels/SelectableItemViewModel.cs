using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LdapCloudSync.App.ViewModels;

/// <summary>
/// A simple wrapper that pairs a string value with a checkbox state.
/// Used for multi-select ListBox items (OU/Group pickers).
/// </summary>
public sealed class SelectableItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private readonly Action? _onChanged;

    public SelectableItemViewModel(string value, bool isSelected = false, Action? onChanged = null)
    {
        Value = value;
        _isSelected = isSelected;
        _onChanged = onChanged;
    }

    public string Value { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            _onChanged?.Invoke();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}