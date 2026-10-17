using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 一个供液槽的显示模型（后端 TankDto），TankScene 绑它
/// </summary>
public class TankModel : ObservableObject
{
    private TankLevel _level;

    /// <summary>
    /// 液位，还没收到推送是 Unknown
    /// </summary>
    public TankLevel Level
    {
        get => _level;
        private set => SetProperty(ref _level, value);
    }

    /// <summary>
    /// LL 信号
    /// </summary>
    public DiSensorModel LL { get; } = new();

    /// <summary>
    /// L 信号
    /// </summary>
    public DiSensorModel L { get; } = new();

    /// <summary>
    /// H 信号
    /// </summary>
    public DiSensorModel H { get; } = new();

    /// <summary>
    /// HH 信号
    /// </summary>
    public DiSensorModel HH { get; } = new();

    /// <summary>
    /// Overfill 信号
    /// </summary>
    public DiSensorModel Overfill { get; } = new();

    /// <summary>
    /// 用推送就地刷新（界面线程调用）
    /// </summary>
    public void Update(TankDto dto)
    {
        LL.Update(dto.LL);
        L.Update(dto.L);
        H.Update(dto.H);
        HH.Update(dto.HH);
        Overfill.Update(dto.Overfill);
        Level = (TankLevel)dto.Level;
    }
}
