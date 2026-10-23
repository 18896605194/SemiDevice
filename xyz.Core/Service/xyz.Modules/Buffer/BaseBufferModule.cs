using System.Globalization;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Modules;

/// <summary>
/// buffer 模块基类
/// </summary>
public abstract class BaseBufferModule : BaseTransferStationModule, IBuffer
{
    #region SC

    [SCEditor("True", "Buffer", "是否启用这一段 buffer（False = 装机未接 / 停用：不登记槽位、不切生产模式，调度不往这儿放片）")]
    public bool IsEnable { get; set; } = true;

    [SCEditor("1", "Buffer", "这一段有几层（晶圆账按它登记槽位）；机械手控制器里每一段各教一个站号，这一段的 1 号槽就是那个站号的第 1 层")]
    public override int SlotCount { get; set; } = 1;

    #endregion

    /// <summary>
    /// 状态：没有要回原点的机构，一开机就是待命
    /// </summary>
    public override int State { get; protected set; } = ModuleState.Idle;

    /// <summary>
    /// 站点类型：buffer
    /// </summary>
    public override StationKind Kind => StationKind.Buffer;

    public override bool InitComponent()
    {
        if (SlotCount < 1)
        {
            throw new InvalidOperationException(
                $"sc.xml 节点 {FullPath} 的 SlotCount={SlotCount.ToString(CultureInfo.InvariantCulture)} 不对：要是正整数。");
        }

        if (!IsEnable)
        {
            return true;
        }

        // 在晶圆账里也是个位置：片放在这一段的哪一层，账上就记在这个站的哪一槽
        IocHelper.GetService<ILedgerManager>()?.RegisterLocation(Name, SlotCount);

        // 没有手动页、没有要人确认的设备：开机就切生产模式，调度才会往这儿派片
        SetProduction();
        return base.InitComponent();
    }

    /// <summary>
    /// 初始化（全部初始化会调）：没有要动的机构，没在跟机械手交互就直接回待命；正在交互被拒（返回 null）
    /// </summary>
    public override ModuleRoutine? InitModule()
    {
        if (!IsEnable)
        {
            return ModuleRoutine.Done(Name, "Init");
        }

        lock (RoutineGate)
        {
            int state = State;
            if (state != ModuleState.NotInit && state != ModuleState.Error && state != StandbyState)
            {
                return null;
            }

            TransferStep(state, StandbyState);
            return ModuleRoutine.Done(Name, "Init");
        }
    }
}
