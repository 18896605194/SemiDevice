using System.Diagnostics.CodeAnalysis;
using xyz.Components.Components;

namespace xyz.Modules;

public interface IRobot
{
    #region 状态

    /// <summary>
    /// 模块名（如 Robot1）。
    /// </summary>
    string Name { get; }

    int State { get; }

    /// <summary>
    /// 是否上使能
    /// </summary>
    bool? IsServoOn { get; }

    /// <summary>
    /// 当前报错（错误码#内容）
    /// </summary>
    string? DeviceError { get; }

    #endregion

    #region 站点

    /// <summary>
    /// 本机械手的站点表：模块名（如 LoadPort1）
    /// </summary>
    IReadOnlyDictionary<string, RobotStation> Stations { get; }

    /// <summary>
    /// 按模块名查站点配置（忽略大小写）；未配置返回 false。
    /// </summary>
    bool TryGetStation(string station, [MaybeNullWhen(false)] out RobotStation config);

    #endregion

    #region 动作

    /// <summary>
    /// 模块初始化（动硬件）
    /// </summary>
    /// <returns></returns>

    ModuleOperation? InitModule();

    ModuleOperation? Home();

    ModuleOperation? Reset();

    ModuleOperation? Abort();

    ModuleOperation? Pick(int arm, string station, int slot);

    ModuleOperation? Place(int arm, string station, int slot);

    ModuleOperation? PowerOn();

    ModuleOperation? PowerOff();

    #endregion
}
