namespace xyz.Components.Interfaces;

/// <summary>
/// 机械手控制器的一根轴（X / Z / Theta，手指也是）：坐标由机械手模块轮询驱动查回来写进来，只出数据、不动硬件。
/// </summary>
public interface IRobotAxis : IDeviceComponent
{
    /// <summary>最近一次查询到的坐标；null = 还没查到。</summary>
    double? Position { get; }

    /// <summary>记下查询回来的坐标（机械手模块的状态轮询调）。</summary>
    void UpdatePosition(double? position);
}
