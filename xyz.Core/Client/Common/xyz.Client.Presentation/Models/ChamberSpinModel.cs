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

    private bool _isBusy;

    /// <summary>
    /// PLC 报忙（在转）
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    private bool _isError;

    /// <summary>
    /// 驱动器报错，三维图据此把卡盘画成红色。
    /// </summary>
    public bool IsError
    {
        get => _isError;
        private set => SetProperty(ref _isError, value);
    }

    private bool _isClockwise = true;

    /// <summary>从上往下看顺时针转。</summary>
    public bool IsClockwise
    {
        get => _isClockwise;
        private set => SetProperty(ref _isClockwise, value);
    }

    /// <summary>
    /// 用推送就地刷新（界面线程调用）；dto 为 null 表示 sc.xml 里没配旋转电机。转向看实际速度的正负，正的算顺时针。
    /// </summary>
    public void Update(ChamberAxisDto? dto)
    {
        if (dto is null)
        {
            IsPresent = false;
            Path = string.Empty;
            IsBusy = false;
            IsError = false;
            return;
        }

        IsPresent = true;
        Path = dto.Path;
        IsBusy = dto.IsBusy;
        IsError = dto.IsError;
        IsClockwise = dto.CurrentSpeed >= 0;
    }
}
