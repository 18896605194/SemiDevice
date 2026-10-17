using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 腔体
/// </summary>
public interface IChamber : IModule
{
    /// <summary>
    /// 站点类型：工艺配方按它挑能跑的腔体
    /// </summary>
    string Type { get; }

    /// <summary>
    /// 这个配方跟腔体里装的对不对得上，对不上带回是哪一项
    /// </summary>
    (string Field, string Value)? FindRecipeMismatch(RecipeData recipe);

    #region 整腔动作和工艺

    /// <summary>
    /// 回零
    /// </summary>
    ModuleOperation? Home();

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

    /// <summary>
    /// 这一步工艺能不能起，能是 null
    /// </summary>
    ProcessRejection? CheckProcess(ProcessRequest request);

    /// <summary>
    /// 起工艺
    /// </summary>
    ModuleOperation? StartProcess(ProcessRequest request);

    #endregion

    #region 手动页的设备动作（按组件全路径找设备）

    /// <summary>
    /// 设备动作等到做完的上限（ms）
    /// </summary>
    int DeviceActionTimeout { get; }

    /// <summary>
    /// 一根轴回零
    /// </summary>
    ChamberDeviceActionResult AxisHome(string path, out ModuleOperation? operation);

    /// <summary>
    /// 一根轴走到绝对位置
    /// </summary>
    ChamberDeviceActionResult AxisMove(string path, double position, double speed, out ModuleOperation? operation);

    /// <summary>
    /// 一根轴走一段（相对）
    /// </summary>
    ChamberDeviceActionResult AxisStep(string path, double distance, double speed, out ModuleOperation? operation);

    /// <summary>
    /// 一根轴复位
    /// </summary>
    ChamberDeviceActionResult AxisReset(string path, out ModuleOperation? operation);

    /// <summary>
    /// 一个气缸升（开）或降（关）
    /// </summary>
    ChamberDeviceActionResult MoveCylinder(string path, bool up, out ModuleOperation? operation);

    /// <summary>
    /// 点动：按住期间界面每隔一会儿续一次（AxisJogRenew）
    /// </summary>
    ChamberDeviceActionResult AxisJog(string path, double speed);

    /// <summary>
    /// 一根轴停下
    /// </summary>
    ChamberDeviceActionResult AxisStop(string path);

    /// <summary>
    /// 点动续一次，没在点动返回 false
    /// </summary>
    bool AxisJogRenew(string path);

    #endregion

    #region 推给界面的

    /// <summary>
    /// 模块状态（推给界面的那一份）
    /// </summary>
    ChamberDto CreateStateDto();

    /// <summary>
    /// 设备状态快照，结构跟 sc 一样
    /// </summary>
    ChamberDeviceDataDto CreateDeviceDataDto();

    #endregion
}
