using System.Diagnostics.CodeAnalysis;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;

namespace xyz.Modules;

public interface IRobot : IModule
{
    #region 状态

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

    // 动作入口按 状态 → 资源 → 互锁 查，被拒带原因（错误码 + 参数）；复位、急停跟着组件基类，被拒返回 null。

    HandleResult<ModuleOperation> Home();

    /// <summary>取片：站点要先由搬运管理准备好（取放都经搬运管理来）。</summary>
    HandleResult<ModuleOperation> Pick(int arm, string station, int slot);

    /// <summary>放片：站点要先由搬运管理准备好。</summary>
    HandleResult<ModuleOperation> Place(int arm, string station, int slot);

    /// <summary>取片能不能做（只查不动，不能做返回原因）：搬运管理落"取放中"标记之前先问。</summary>
    HandleResult? CheckPick(int arm, string station, int slot);

    /// <summary>放片能不能做（只查不动，不能做返回原因）。</summary>
    HandleResult? CheckPlace(int arm, string station, int slot);

    /// <summary>
    /// 换片：在同一个站点槽位上，先用 pickArm 把槽里的片取出来，再把 placeArm 上的片放进去，中间不离开站点。
    /// 不支持换片的机械手返回 null（被拒）。
    /// </summary>
    ModuleOperation? Swap(int pickArm, int placeArm, string station, int slot);

    HandleResult<ModuleOperation> PowerOn();

    HandleResult<ModuleOperation> PowerOff();

    #endregion

    #region 给服务用的（手动页、主界面）

    /// <summary>
    /// 回原点等到做完的上限（ms）
    /// </summary>
    int HomeTimeout { get; }

    /// <summary>
    /// 复位等到做完的上限（ms）
    /// </summary>
    int ResetTimeout { get; }

    /// <summary>
    /// 中止等到做完的上限（ms）
    /// </summary>
    int AbortTimeout { get; }

    /// <summary>
    /// 上电、断电等到做完的上限（ms）
    /// </summary>
    int PowerTimeout { get; }

    /// <summary>
    /// 复位能不能做，能是 null
    /// </summary>
    HandleResult? CheckReset();

    /// <summary>
    /// 中止能不能做，能是 null
    /// </summary>
    HandleResult? CheckAbort();

    /// <summary>
    /// 现在的状态（推给界面的那一份）
    /// </summary>
    RobotDto CreateStateDto();

    #endregion
}
