using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 旋转电机（卡盘）的显示模型：腔体三维图绑它转盘面。sc.xml 里没配时 IsPresent 为 false。
/// </summary>
public class ChamberSpinModel : ObservableObject
{
    private string _path = string.Empty;

    /// <summary>组件全路径，如 "Chamber1.SpinMotor"。</summary>
    public string Path
    {
        get => _path;
        private set => SetProperty(ref _path, value);
    }

    private bool _isPresent;

    /// <summary>sc.xml 里配了旋转电机。</summary>
    public bool IsPresent
    {
        get => _isPresent;
        private set => SetProperty(ref _isPresent, value);
    }

    private bool _isSpinning;

    /// <summary>在转。</summary>
    public bool IsSpinning
    {
        get => _isSpinning;
        private set => SetProperty(ref _isSpinning, value);
    }

    private bool _isClockwise = true;

    /// <summary>从上往下看顺时针转。</summary>
    public bool IsClockwise
    {
        get => _isClockwise;
        private set => SetProperty(ref _isClockwise, value);
    }

    /// <summary>
    /// 用推送就地刷新（界面线程调用）；dto 为 null 表示 sc.xml 里没配旋转电机。
    /// </summary>
    public void Update(ChamberSpinDto? dto)
    {
        if (dto is null)
        {
            IsPresent = false;
            Path = string.Empty;
            IsSpinning = false;
            return;
        }

        IsPresent = true;
        Path = dto.Path;
        IsSpinning = dto.IsSpinning;
        IsClockwise = dto.IsClockwise;
    }
}
