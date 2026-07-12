using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoClickerPro.ViewModels;

/// <summary>
/// Common base for all ViewModels: standard INotifyPropertyChanged with a SetField helper
/// that avoids redundant property-changed notifications when the value hasn't actually changed.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
