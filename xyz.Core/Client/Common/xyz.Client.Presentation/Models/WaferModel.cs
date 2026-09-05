using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// Wafer 数据模型。
/// </summary>
public class WaferModel : INotifyPropertyChanged
{
    private int _slot;
    public int Slot
    {
        get => _slot;
        set => SetProperty(ref _slot, value);
    }

    private string _state = string.Empty;
    public string State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    private string _loadPort = string.Empty;
    public string LoadPort
    {
        get => _loadPort;
        set => SetProperty(ref _loadPort, value);
    }

    private string _lpSlot = string.Empty;
    public string LpSlot
    {
        get => _lpSlot;
        set => SetProperty(ref _lpSlot, value);
    }

    private string _time = string.Empty;
    public string Time
    {
        get => _time;
        set => SetProperty(ref _time, value);
    }

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
