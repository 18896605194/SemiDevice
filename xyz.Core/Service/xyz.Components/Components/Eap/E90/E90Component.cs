using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Secs.Gem;
using xyz.Secs.Gem.E90;

namespace xyz.Components.Components;

/// <summary>
/// 片跟踪（SEMI E90，machine.xml 的 Eap 下的 E90 节点）：协议（片和片位的状态、事件、E39 对象）在 xyz.Secs 的 E90Engine，
/// 这里把晶圆账交给它（IE90Ledger），账的变化（IE90Callback）翻成它的片交进去。片在哪、做到哪只有账一份是真的
/// </summary>
[Component(description: "片跟踪（SEMI E90）：按晶圆账报片的位置、工艺状态和片位占用")]
public class E90Component : ComponentBase, IE90Callback, IE90Component, IE90Ledger, IGemEvents
{
    #region DV、事件

    [DataVariable(ValueFormat.String, "片号")]
    public readonly string SubstIdData = E90Codes.SubstId;

    [DataVariable(ValueFormat.String, "片的批次号")]
    public readonly string LotIdData = E90Codes.LotId;

    [DataVariable(ValueFormat.String, "片现在在哪（片位号）")]
    public readonly string LocationIdData = E90Codes.LocationId;

    [DataVariable(ValueFormat.Int, "片的位置状态：0 在来源载具、1 在机内、2 回到载具")]
    public readonly string StateData = E90Codes.State;

    [DataVariable(ValueFormat.Int, "片的工艺状态：0 要做、1 在做、2 做完、3 中止、5 没做成、7 跳过")]
    public readonly string ProcStateData = E90Codes.ProcState;

    [DataVariable(ValueFormat.String, "片从哪来（片位号）")]
    public readonly string SourceData = E90Codes.Source;

    [DataVariable(ValueFormat.String, "片回哪去（片位号）")]
    public readonly string DestinationData = E90Codes.Destination;

    [DataVariable(ValueFormat.Int, "片位状态：0 空、1 有片")]
    public readonly string LocStateData = E90Codes.LocState;

    [DataVariable(ValueFormat.String, "片位上的片号（空为空）")]
    public readonly string LocSubstIdData = E90Codes.LocSubstId;

    [EventAttribut("片对象建了，在来源载具（#1）", Data = new[] { E90Codes.SubstId, E90Codes.LotId, E90Codes.LocationId, E90Codes.State, E90Codes.Source, E90Codes.Destination })]
    public readonly string SubstTrans01 = "SubstSMTrans01";

    [EventAttribut("片离开来源载具进机内（#2）", Data = new[] { E90Codes.SubstId, E90Codes.LotId, E90Codes.LocationId, E90Codes.State, E90Codes.ProcState })]
    public readonly string SubstTrans02 = "SubstSMTrans02";

    [EventAttribut("片在机内换位置（#4）", Data = new[] { E90Codes.SubstId, E90Codes.LotId, E90Codes.LocationId, E90Codes.State, E90Codes.ProcState })]
    public readonly string SubstTrans04 = "SubstSMTrans04";

    [EventAttribut("片回到载具（#5）", Data = new[] { E90Codes.SubstId, E90Codes.LotId, E90Codes.LocationId, E90Codes.State, E90Codes.ProcState, E90Codes.Destination })]
    public readonly string SubstTrans05 = "SubstSMTrans05";

    [EventAttribut("片从载具又进机内（#6）", Data = new[] { E90Codes.SubstId, E90Codes.LotId, E90Codes.LocationId, E90Codes.State, E90Codes.ProcState })]
    public readonly string SubstTrans06 = "SubstSMTrans06";

    [EventAttribut("片对象删了，在载具里（#7：载具拿走）", Data = new[] { E90Codes.SubstId, E90Codes.LotId, E90Codes.LocationId, E90Codes.State, E90Codes.ProcState })]
    public readonly string SubstTrans07 = "SubstSMTrans07";

    [EventAttribut("片对象删了，不在载具里（#9：人工删片）", Data = new[] { E90Codes.SubstId, E90Codes.LotId, E90Codes.LocationId, E90Codes.State, E90Codes.ProcState })]
    public readonly string SubstTrans09 = "SubstSMTrans09";

    [EventAttribut("片要做（#10）", Data = new[] { E90Codes.SubstId, E90Codes.LotId, E90Codes.ProcState })]
    public readonly string SubstTrans10 = "SubstSMTrans10";

    [EventAttribut("片开始做（#11）", Data = new[] { E90Codes.SubstId, E90Codes.LotId, E90Codes.LocationId, E90Codes.ProcState })]
    public readonly string SubstTrans11 = "SubstSMTrans11";

    [EventAttribut("片做完了 / 中止 / 没做成（#12）", Data = new[] { E90Codes.SubstId, E90Codes.LotId, E90Codes.LocationId, E90Codes.ProcState })]
    public readonly string SubstTrans12 = "SubstSMTrans12";

    [EventAttribut("片没做就回来了，记跳过（#14）", Data = new[] { E90Codes.SubstId, E90Codes.LotId, E90Codes.LocationId, E90Codes.ProcState })]
    public readonly string SubstTrans14 = "SubstSMTrans14";

    [EventAttribut("片位有片了", Data = new[] { E90Codes.LocationId, E90Codes.LocState, E90Codes.LocSubstId })]
    public readonly string LocationOccupied = E90Codes.LocationOccupied;

    [EventAttribut("片位空了", Data = new[] { E90Codes.LocationId, E90Codes.LocState, E90Codes.LocSubstId })]
    public readonly string LocationUnoccupied = E90Codes.LocationUnoccupied;

    #endregion

    private ILedgerManager? _ledger;

    /// <summary>
    /// 片跟踪的协议部分；没接为 null
    /// </summary>
    public E90Engine? Engine { get; private set; }

    #region 接设备

    /// <summary>
    /// 接到晶圆账上（EAP 组件在链路打开之前调）：建 E90 的协议部分（登记 E39 对象类型，账上已有的片不报事件、直接建影子），挂 E90 上报口。
    /// waitForCarrier：接了 E87 时为 true——LoadPort 上的片等槽图认定才建片对象
    /// </summary>
    public void Attach(IE39Component? objects, ILedgerManager ledger, bool waitForCarrier)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        _ledger = ledger;
        Engine = new E90Engine(objects?.Engine, this, this, waitForCarrier, new SecsLog(this), Name);
        ledger.E90Callback = this;
    }

    /// <summary>
    /// 从晶圆账上摘下来（宿主退出时）
    /// </summary>
    public void Detach()
    {
        if (_ledger is not null && ReferenceEquals(_ledger.E90Callback, this))
        {
            _ledger.E90Callback = null;
        }

        Engine = null;
    }

    #endregion

    #region 账本回调（IE90Callback，在 EAP 的上报派发线程上）

    void IE90Callback.WaferCreated(WaferInfo wafer)
    {
        Engine?.WaferCreated(WaferOf(wafer));
    }

    void IE90Callback.WaferMoved(WaferInfo wafer, string fromModule, int fromSlot)
    {
        Engine?.WaferMoved(WaferOf(wafer), fromModule, fromSlot);
    }

    void IE90Callback.WaferUpdated(WaferInfo wafer)
    {
        Engine?.WaferUpdated(WaferOf(wafer));
    }

    void IE90Callback.WaferDeleted(WaferInfo wafer)
    {
        Engine?.WaferDeleted(WaferOf(wafer));
    }

    void IGemEvents.Report(string code, params (string Name, object? Value)[] data)
    {
        RaiseEvent(code, data.Select(item => new GemData(item.Name, item.Value)).ToArray());
    }

    #endregion

    #region 晶圆账（IE90Ledger）

    IReadOnlyList<(string Module, int SlotCount)> IE90Ledger.Locations => LedgerOrThrow().Locations;

    bool IE90Ledger.IsLoadPort(string module)
    {
        return LedgerOrThrow().IsLoadPort(module);
    }

    IReadOnlyList<E90Wafer> IE90Ledger.WafersAt(string module)
    {
        var wafers = new List<E90Wafer>();
        foreach (var wafer in LedgerOrThrow().GetSlots(module))
        {
            if (wafer is not null)
            {
                wafers.Add(WaferOf(wafer));
            }
        }

        return wafers;
    }

    E90Wafer? IE90Ledger.WaferAt(string module, int slot)
    {
        var wafer = LedgerOrThrow().Get(module, slot);
        if (wafer is null)
        {
            return null;
        }

        return WaferOf(wafer);
    }

    private ILedgerManager LedgerOrThrow()
    {
        if (_ledger is null)
        {
            throw new InvalidOperationException($"{Name} 还没接晶圆账");
        }

        return _ledger;
    }

    /// <summary>
    /// 账上的一片翻成 E90 的片：来源是来源 LoadPort 的槽（片从载具来），没有就是片最早在的地方
    /// </summary>
    private static E90Wafer WaferOf(WaferInfo wafer)
    {
        string sourceModule = wafer.OriginModule;
        int sourceSlot = wafer.OriginSlot;
        if (wafer.SourceLoadPort is not null)
        {
            sourceModule = wafer.SourceLoadPort;
            sourceSlot = wafer.SourceSlot;
        }

        return new E90Wafer(wafer.Id, wafer.WaferId, wafer.LotId ?? string.Empty, wafer.Module, wafer.Slot, sourceModule, sourceSlot,
            ProcessOf(wafer.ProcessState));
    }

    private static E90WaferProcess ProcessOf(WaferProcessState state)
    {
        switch (state)
        {
            case WaferProcessState.InProcess:
                return E90WaferProcess.InProcess;
            case WaferProcessState.Completed:
                return E90WaferProcess.Completed;
            case WaferProcessState.Failed:
                return E90WaferProcess.Failed;
            case WaferProcessState.Aborted:
                return E90WaferProcess.Aborted;
            default:
                return E90WaferProcess.Idle;
        }
    }

    #endregion
}
