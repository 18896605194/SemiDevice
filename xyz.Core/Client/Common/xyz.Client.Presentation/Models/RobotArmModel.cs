using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 机械手单个手臂（手指）的显示数据。各手臂独立：改哪只手臂的伸出量，只有那只手臂动。
/// </summary>
public class RobotArmModel : INotifyPropertyChanged
{
    private int _arm;

    /// <summary>
    /// 手臂号，从 1 开始。
    /// </summary>
    public int Arm
    {
        get => _arm;
        set => SetProperty(ref _arm, value);
    }

    private double _extension;

    /// <summary>
    /// 伸出量：0 = 收回，1 = 伸到位；中间值按比例显示，可直接接轴位置反馈。
    /// </summary>
    public double Extension
    {
        get => _extension;
        set
        {
            if (SetProperty(ref _extension, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(IsExtended));
            }
        }
    }

    /// <summary>
    /// 是否伸出（伸出量大于 0）。
    /// </summary>
    public bool IsExtended => _extension > 0;

    private double _heading;

    /// <summary>
    /// 手臂相对转台正前方的朝向（度，顺时针）；蛙式双臂背靠背时，另一只手臂填 180。
    /// </summary>
    public double Heading
    {
        get => _heading;
        set => SetProperty(ref _heading, value);
    }

    private WaferModel? _wafer;

    /// <summary>
    /// 手臂上的片；null 表示空手。片的颜色按 WaferModel.State，片上显示 LpSlot。
    /// </summary>
    public WaferModel? Wafer
    {
        get => _wafer;
        set
        {
            if (SetProperty(ref _wafer, value))
            {
                OnPropertyChanged(nameof(HasWafer));
            }
        }
    }

    /// <summary>
    /// 手臂上是否有片。
    /// </summary>
    public bool HasWafer => _wafer is not null;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
