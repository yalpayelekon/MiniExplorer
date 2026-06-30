using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MiniExplorer.Localization;

public sealed class LocalizationNotifier : INotifyPropertyChanged
{
    public static LocalizationNotifier Instance { get; } = new();

    private int _revision;

    public int Revision
    {
        get => _revision;
        private set
        {
            if (_revision != value)
            {
                _revision = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void BumpRevision() => Revision++;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
