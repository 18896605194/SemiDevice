using xyz.Components.Components;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 腔体：带卡盘、摆臂、气缸的那种，腔体手动页按它认；工艺那几个在 IProcessStation
/// </summary>
public interface IChamber : IProcessStation
{
    #region 整腔动作

    /// <summary>
    /// 回零：状态 → 资源 → 互锁查过才挂上，被拒带原因返回
    /// </summary>
    HandleResult<ModuleRoutine> Home();

    /// <summary>
    /// 复位被拒的原因（复位本身是 IModule.Reset，签名跟着组件基类）
    /// </summary>
    HandleResult? CheckReset();

    /// <summary>
    /// 中止被拒的原因（中止本身是 IModule.Abort，签名跟着组件基类）
    /// </summary>
    HandleResult? CheckAbort();

    /// <summary>
    /// 回零等到做完的上限（ms）
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

    #endregion

    #region 手动页的设备动作（按组件全路径找设备）

    /// <summary>
    /// 设备动作等到做完的上限（ms）
    /// </summary>
    int DeviceActionTimeout { get; }

    /// <summary>
    /// 一根轴回零
    /// </summary>
    HandleResult<ModuleRoutine> AxisHome(string path);

    /// <summary>
    /// 一根轴走到绝对位置；speed 为 0 按这根轴的 EC MoveSpeed
    /// </summary>
    HandleResult<ModuleRoutine> AxisMove(string path, double position, double speed);

    /// <summary>
    /// 一根轴走一段（相对）；speed 为 0 按这根轴的 EC MoveSpeed
    /// </summary>
    HandleResult<ModuleRoutine> AxisStep(string path, double distance, double speed);

    /// <summary>
    /// 一根轴复位
    /// </summary>
    HandleResult<ModuleRoutine> AxisReset(string path);

    /// <summary>
    /// 一个气缸升（开）或降（关）
    /// </summary>
    HandleResult<ModuleRoutine> MoveCylinder(string path, bool up);

    /// <summary>
    /// 点动：按下发一次就回，返回的是等松手的动作；一直走到松手发 AxisStop
    /// </summary>
    HandleResult<ModuleRoutine> AxisJog(string path, double speed);

    /// <summary>
    /// 一根轴停下：不看腔体忙不忙、不挂动作，发出去就回
    /// </summary>
    HandleResult AxisStop(string path);

    #endregion

    #region 推给界面的

    /// <summary>
    /// 模块状态（推给界面的那一份）
    /// </summary>
    ChamberDto CreateStateDto();

    /// <summary>
    /// 设备状态快照，结构跟 machine.xml 一样
    /// </summary>
    ChamberDeviceDataDto CreateDeviceDataDto();

    #endregion
}
