using xyz.Components.Attributes;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz._35021.Module.Clean.Operation;

namespace xyz._35021.Module.Clean;

/// <summary>
/// 35021 机台清洗腔模块。
///
/// ⚠ 驱动还没接：四个动作眼下都是按下面两个常量计时、时间到就算成功的空转操作，
/// 腔体不连任何设备。目的只是把装配、站点交互环、搬运这条链路先跑起来。
/// 驱动定下来之后：重写 OpenDevice 连设备，把 TimedOperation 逐个换成
/// 发指令 + 等回复的真操作（照 Robot 的 HomeOperation 写），换完把常量和 TimedOperation 一起删。
/// </summary>
[Component(description: "35021 清洗腔模块")]
public class ChamberModule : BaseChamberModule
{
    /// <summary>未接驱动时 Home/Reset/Abort 的假动作时长。</summary>
    private const int SimulateActionMs = 1000;

    /// <summary>未接驱动时一支配方的假工艺时长。</summary>
    private const int SimulateProcessMs = 5000;

    #region Action

    public override ModuleOperation? Home()
    {
        return Begin(ChamberAction.Home, new TimedOperation("Home", SimulateActionMs));
    }

    protected override ModuleOperation? ResetDevice()
    {
        return Begin(ChamberAction.Reset, new TimedOperation("Reset", SimulateActionMs));
    }

    protected override ModuleOperation? AbortDevice()
    {
        return Begin(ChamberAction.Abort, new TimedOperation("Abort", SimulateActionMs));
    }

    public override ModuleOperation? Process(string recipe)
    {
        return Begin(ChamberAction.Process, new TimedOperation($"Process {recipe}", SimulateProcessMs));
    }

    #endregion
}
