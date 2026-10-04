using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 一个双作用气缸（门、Bowl、Lift）的显示模型：腔体三维图绑它画开关 / 升降。sc.xml 里没配时 IsPresent 为 false。
/// 三维图跟反馈走：到位画在那一头，未知（命令发了、到位信号还没亮）画在行程中间并高亮。
/// </summary>
public class ChamberCylinderModel : ObservableObject
{
    private string _path = string.Empty;

    /// <summary>组件全路径，如 "Chamber1.Door"。</summary>
    public string Path
    {
        get => _path;
        private set => SetProperty(ref _path, value);
    }

    private bool _isPresent;

    /// <summary>sc.xml 里配了这个部件。</summary>
    public bool IsPresent
    {
        get => _isPresent;
        private set
        {
            if (SetProperty(ref _isPresent, value))
            {
                OnPropertyChanged(nameof(IsUnknown));
            }
        }
    }

    private TwoStatePosition _position;

    /// <summary>在哪一侧：开到位、关到位、未知。</summary>
    public TwoStatePosition Position
    {
        get => _position;
        private set
        {
            if (SetProperty(ref _position, value))
            {
                OnPropertyChanged(nameof(IsOpen));
                OnPropertyChanged(nameof(IsUnknown));
            }
        }
    }

    /// <summary>开到位（门开、Bowl 升、Lift 升）。</summary>
    public bool IsOpen => Position == TwoStatePosition.Opened;

    /// <summary>未知：三维图画在行程中间、高亮。</summary>
    public bool IsUnknown => IsPresent && Position == TwoStatePosition.Unknown;

    /// <summary>
    /// 用推送就地刷新（界面线程调用）；dto 为 null 表示 sc.xml 里没配这个部件。
    /// </summary>
    public void Update(PartDto? dto)
    {
        if (dto is null)
        {
            IsPresent = false;
            Path = string.Empty;
            Position = TwoStatePosition.Unknown;
            return;
        }

        IsPresent = true;
        Path = dto.Path;
        Position = ParsePosition(dto.Get(PartValueNames.Position));
    }

    /// <summary>推送里的 Position 原文转枚举；认不出来算未知。</summary>
    public static TwoStatePosition ParsePosition(string text)
    {
        return Enum.TryParse(text, out TwoStatePosition position) ? position : TwoStatePosition.Unknown;
    }
}
