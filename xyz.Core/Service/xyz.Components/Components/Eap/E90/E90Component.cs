using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// 片跟踪（SEMI E90，sc.xml 的 Eap 下的 E90 节点）：按晶圆账报片的位置和工艺状态。片在哪、做到哪只有账本一份是真的，这里照着报。
/// ① 片对象：载具的槽图认定了（E87 说料到了）才有——Host 给的片号先写进账，片号才定；不在 LoadPort 上建的片（人工建在腔体里）建了就有。
///    没接 E87 时建片就有。
/// ② 片的位置（E90 Transport）：在来源载具（AT SOURCE）→ 机内（AT WORK）→ 回到载具（AT DESTINATION）；
///    片的工艺（E90 Processing）：要做（NEEDS PROCESSING）→ 在做（IN PROCESS）→ 做完（PROCESSED）/ 中止（ABORTED）/ 没做成（REJECTED），
///    出去转了一圈没做就回来的记跳过（SKIPPED）。
/// ③ 片位（SubstLoc）：每个槽、手指、腔体一个，有片没片各报一个事件。
/// ④ E39 对象 Substrate、SubstLoc（Host 用 S14F1 查）。没有读片号的设备，片号核对（ID Status）不做。
/// </summary>
[Component(description: "片跟踪（SEMI E90）：按晶圆账报片的位置、工艺状态和片位占用")]
public class E90Component : ComponentBase, IE90Callback
{
    #region 状态码（E90，U1）

    private const byte AtSource = 0;
    private const byte AtWork = 1;
    private const byte AtDestination = 2;

    private const byte NeedsProcessing = 0;
    private const byte InProcess = 1;
    private const byte Processed = 2;
    private const byte Aborted = 3;
    private const byte Rejected = 5;
    private const byte Skipped = 7;

    private const byte Unoccupied = 0;
    private const byte Occupied = 1;

    #endregion

    #region DV、事件

    private const string DvSubstId = "SubstID";
    private const string DvLotId = "SubstLotID";
    private const string DvLocationId = "SubstLocID";
    private const string DvState = "SubstState";
    private const string DvProcState = "SubstProcState";
    private const string DvSource = "SubstSource";
    private const string DvDestination = "SubstDestination";
    private const string DvLocState = "SubstLocState";
    private const string DvLocSubstId = "SubstLocSubstID";

    [DataVariable(ValueFormat.String, "片号")]
    public readonly string SubstIdData = DvSubstId;

    [DataVariable(ValueFormat.String, "片的批次号")]
    public readonly string LotIdData = DvLotId;

    [DataVariable(ValueFormat.String, "片现在在哪（片位号）")]
    public readonly string LocationIdData = DvLocationId;

    [DataVariable(ValueFormat.Int, "片的位置状态：0 在来源载具、1 在机内、2 回到载具")]
    public readonly string StateData = DvState;

    [DataVariable(ValueFormat.Int, "片的工艺状态：0 要做、1 在做、2 做完、3 中止、5 没做成、7 跳过")]
    public readonly string ProcStateData = DvProcState;

    [DataVariable(ValueFormat.String, "片从哪来（片位号）")]
    public readonly string SourceData = DvSource;

    [DataVariable(ValueFormat.String, "片回哪去（片位号）")]
    public readonly string DestinationData = DvDestination;

    [DataVariable(ValueFormat.Int, "片位状态：0 空、1 有片")]
    public readonly string LocStateData = DvLocState;

    [DataVariable(ValueFormat.String, "片位上的片号（空为空）")]
    public readonly string LocSubstIdData = DvLocSubstId;

    [EventAttribut("片对象建了，在来源载具（#1）", Data = new[] { DvSubstId, DvLotId, DvLocationId, DvState, DvSource, DvDestination })]
    public readonly string SubstTrans01 = "SubstSMTrans01";

    [EventAttribut("片离开来源载具进机内（#2）", Data = new[] { DvSubstId, DvLotId, DvLocationId, DvState, DvProcState })]
    public readonly string SubstTrans02 = "SubstSMTrans02";

    [EventAttribut("片在机内换位置（#4）", Data = new[] { DvSubstId, DvLotId, DvLocationId, DvState, DvProcState })]
    public readonly string SubstTrans04 = "SubstSMTrans04";

    [EventAttribut("片回到载具（#5）", Data = new[] { DvSubstId, DvLotId, DvLocationId, DvState, DvProcState, DvDestination })]
    public readonly string SubstTrans05 = "SubstSMTrans05";

    [EventAttribut("片从载具又进机内（#6）", Data = new[] { DvSubstId, DvLotId, DvLocationId, DvState, DvProcState })]
    public readonly string SubstTrans06 = "SubstSMTrans06";

    [EventAttribut("片对象删了，在载具里（#7：载具拿走）", Data = new[] { DvSubstId, DvLotId, DvLocationId, DvState, DvProcState })]
    public readonly string SubstTrans07 = "SubstSMTrans07";

    [EventAttribut("片对象删了，不在载具里（#9：人工删片）", Data = new[] { DvSubstId, DvLotId, DvLocationId, DvState, DvProcState })]
    public readonly string SubstTrans09 = "SubstSMTrans09";

    [EventAttribut("片要做（#10）", Data = new[] { DvSubstId, DvLotId, DvProcState })]
    public readonly string SubstTrans10 = "SubstSMTrans10";

    [EventAttribut("片开始做（#11）", Data = new[] { DvSubstId, DvLotId, DvLocationId, DvProcState })]
    public readonly string SubstTrans11 = "SubstSMTrans11";

    [EventAttribut("片做完了 / 中止 / 没做成（#12）", Data = new[] { DvSubstId, DvLotId, DvLocationId, DvProcState })]
    public readonly string SubstTrans12 = "SubstSMTrans12";

    [EventAttribut("片没做就回来了，记跳过（#14）", Data = new[] { DvSubstId, DvLotId, DvLocationId, DvProcState })]
    public readonly string SubstTrans14 = "SubstSMTrans14";

    [EventAttribut("片位有片了", Data = new[] { DvLocationId, DvLocState, DvLocSubstId })]
    public readonly string LocationOccupied = "SubstLocSMTrans01";

    [EventAttribut("片位空了", Data = new[] { DvLocationId, DvLocState, DvLocSubstId })]
    public readonly string LocationUnoccupied = "SubstLocSMTrans02";

    #endregion

    /// <summary>片的历史一条：在哪、进的时刻、出的时刻（还在就是空）。</summary>
    private sealed record Visit(string Location, DateTime TimeIn, DateTime? TimeOut);

    /// <summary>E90 的一片（账上那一片的报告用影子，只在锁里改）。</summary>
    private sealed class Substrate
    {
        public required Guid Id { get; init; }

        public required string SubstId { get; set; }

        public string LotId { get; set; } = string.Empty;

        public required string Location { get; set; }

        public required string Source { get; init; }

        public required string Destination { get; init; }

        public byte State { get; set; } = AtSource;

        public byte ProcState { get; set; } = NeedsProcessing;

        public List<Visit> History { get; } = [];
    }

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Substrate> _substrates = new();
    private E30Component? _gem;
    private WaferManagerComponent? _ledger;
    private bool _waitForCarrier;

    #region 接设备

    /// <summary>
    /// 接到晶圆账上（EAP 组件在链路打开之前调）：挂 E90 上报口，登记 E39 对象类型；账上已经有的片（开机恢复的）不报事件、直接建影子。
    /// waitForCarrier：接了 E87 时为 true——LoadPort 上的片等槽图认定（MaterialArrived）才建片对象。
    /// </summary>
    public void Attach(E30Component gem, E39Component? objects, WaferManagerComponent ledger, bool waitForCarrier)
    {
        ArgumentNullException.ThrowIfNull(gem);
        ArgumentNullException.ThrowIfNull(ledger);
        _gem = gem;
        _ledger = ledger;
        _waitForCarrier = waitForCarrier;
        lock (_gate)
        {
            foreach (var (module, _) in ledger.Locations)
            {
                foreach (var wafer in ledger.GetSlots(module))
                {
                    if (wafer is not null && !(waitForCarrier && ledger.IsLoadPort(wafer.Module)))
                    {
                        Track(wafer, report: false);
                    }
                }
            }
        }

        ledger.E90Callback = this;
        objects?.Register(new SubstrateType(this));
        objects?.Register(new LocationType(this));
        LogHelper.Info(Name, $"E90 接上晶圆账，现有 {_substrates.Count} 片");
    }

    /// <summary>从晶圆账上摘下来（宿主退出时）。</summary>
    public void Detach()
    {
        var ledger = _ledger;
        if (ledger is not null && ReferenceEquals(ledger.E90Callback, this))
        {
            ledger.E90Callback = null;
        }
    }

    /// <summary>
    /// 料到了（E87 槽图认定，在 E87 的线程上调）：这个 LoadPort 上还没有片对象的片都建起来（#1、#10），片号这时已经是 Host 给的了。
    /// </summary>
    public void MaterialArrived(string loadPort)
    {
        var ledger = _ledger;
        if (ledger is null)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var wafer in ledger.GetSlots(loadPort))
            {
                if (wafer is not null && !_substrates.ContainsKey(wafer.Id))
                {
                    Track(wafer, report: true);
                    ReportLocation(LocationOccupied, LocationOf(wafer.Module, wafer.Slot), wafer.WaferId);
                }
            }
        }
    }

    #endregion

    #region 账本回调（IE90Callback，在账本的 EAP 派发线程上）

    void IE90Callback.WaferCreated(WaferInfo wafer)
    {
        lock (_gate)
        {
            if (_waitForCarrier && _ledger?.IsLoadPort(wafer.Module) == true)
            {
                return;
            }

            Track(wafer, report: true);
            ReportLocation(LocationOccupied, LocationOf(wafer.Module, wafer.Slot), wafer.WaferId);
        }
    }

    void IE90Callback.WaferMoved(WaferInfo wafer, string fromModule, int fromSlot)
    {
        lock (_gate)
        {
            string from = LocationOf(fromModule, fromSlot);
            string to = LocationOf(wafer.Module, wafer.Slot);
            ReportLocation(LocationUnoccupied, from, string.Empty);
            ReportLocation(LocationOccupied, to, wafer.WaferId);

            if (!_substrates.TryGetValue(wafer.Id, out var substrate))
            {
                // 还没有片对象（载具没认定就被人挪出来了）：从现在起跟踪
                substrate = Track(wafer, report: true);
            }

            var now = DateTime.Now;
            if (substrate.History.Count > 0)
            {
                var last = substrate.History[^1];
                substrate.History[^1] = last with { TimeOut = now };
            }

            substrate.History.Add(new Visit(to, now, null));
            substrate.Location = to;
            bool toCarrier = _ledger?.IsLoadPort(wafer.Module) == true;
            switch (substrate.State)
            {
                case AtSource:
                    Move(substrate, AtWork, SubstTrans02);
                    if (toCarrier)
                    {
                        Arrive(substrate);
                    }

                    break;

                case AtWork:
                    if (toCarrier)
                    {
                        Arrive(substrate);
                    }
                    else
                    {
                        Move(substrate, AtWork, SubstTrans04);
                    }

                    break;

                default:
                    Move(substrate, AtWork, SubstTrans06);
                    if (toCarrier)
                    {
                        Arrive(substrate);
                    }

                    break;
            }
        }
    }

    void IE90Callback.WaferUpdated(WaferInfo wafer)
    {
        lock (_gate)
        {
            if (!_substrates.TryGetValue(wafer.Id, out var substrate))
            {
                return;
            }

            substrate.SubstId = wafer.WaferId;
            substrate.LotId = wafer.LotId ?? string.Empty;
            byte next = wafer.ProcessState switch
            {
                WaferProcessState.InProcess => InProcess,
                WaferProcessState.Completed => Processed,
                WaferProcessState.Aborted => Aborted,
                WaferProcessState.Failed => Rejected,
                _ => substrate.ProcState,
            };
            if (next == substrate.ProcState)
            {
                return;
            }

            substrate.ProcState = next;
            ReportSubstrate(next == InProcess ? SubstTrans11 : SubstTrans12, substrate);
        }
    }

    void IE90Callback.WaferDeleted(WaferInfo wafer)
    {
        lock (_gate)
        {
            if (!_substrates.Remove(wafer.Id, out var substrate))
            {
                return;
            }

            ReportLocation(LocationUnoccupied, LocationOf(wafer.Module, wafer.Slot), string.Empty);
            ReportSubstrate(substrate.State == AtDestination ? SubstTrans07 : SubstTrans09, substrate);
        }
    }

    #endregion

    #region 状态推进（在锁里）

    /// <summary>建片影子；report 为 true 时报 #1（在来源）、#10（要做）。</summary>
    private Substrate Track(WaferInfo wafer, bool report)
    {
        string location = LocationOf(wafer.Module, wafer.Slot);
        string source = wafer.SourceLoadPort is null ? LocationOf(wafer.OriginModule, wafer.OriginSlot) : LocationOf(wafer.SourceLoadPort, wafer.SourceSlot);
        bool inCarrier = _ledger?.IsLoadPort(wafer.Module) == true;
        var substrate = new Substrate
        {
            Id = wafer.Id,
            SubstId = wafer.WaferId,
            LotId = wafer.LotId ?? string.Empty,
            Location = location,
            Source = source,
            Destination = source,
            State = !inCarrier ? AtWork : string.Equals(location, source, StringComparison.OrdinalIgnoreCase) ? AtSource : AtDestination,
            ProcState = wafer.ProcessState switch
            {
                WaferProcessState.InProcess => InProcess,
                WaferProcessState.Completed => Processed,
                WaferProcessState.Aborted => Aborted,
                WaferProcessState.Failed => Rejected,
                _ => NeedsProcessing,
            },
        };
        substrate.History.Add(new Visit(location, DateTime.Now, null));
        _substrates[wafer.Id] = substrate;
        if (report)
        {
            ReportSubstrate(SubstTrans01, substrate);
            ReportSubstrate(SubstTrans10, substrate);
        }

        return substrate;
    }

    private void Move(Substrate substrate, byte state, string code)
    {
        substrate.State = state;
        ReportSubstrate(code, substrate);
    }

    /// <summary>回到载具（#5）；出去转了一圈还是"要做"的记跳过（#14）。</summary>
    private void Arrive(Substrate substrate)
    {
        Move(substrate, AtDestination, SubstTrans05);
        if (substrate.ProcState == NeedsProcessing)
        {
            substrate.ProcState = Skipped;
            ReportSubstrate(SubstTrans14, substrate);
        }
    }

    /// <summary>
    /// 片位号：一个槽的位置（腔体、单手指）直接用模块名，多槽的用"模块名.两位槽号"（LoadPort1.05、Robot.01）。
    /// </summary>
    private string LocationOf(string module, int slot)
    {
        int slots = _ledger?.Locations.FirstOrDefault(location => string.Equals(location.Module, module, StringComparison.OrdinalIgnoreCase)).SlotCount ?? 0;
        return slots == 1 ? module : $"{module}.{slot.ToString("00", CultureInfo.InvariantCulture)}";
    }

    #endregion

    #region 报事件

    private void ReportSubstrate(string code, Substrate substrate)
    {
        _gem?.Report(this, code,
            new GemData(DvSubstId, GemValue.Ascii(substrate.SubstId)),
            new GemData(DvLotId, GemValue.Ascii(substrate.LotId)),
            new GemData(DvLocationId, GemValue.Ascii(substrate.Location)),
            new GemData(DvState, substrate.State),
            new GemData(DvProcState, substrate.ProcState),
            new GemData(DvSource, GemValue.Ascii(substrate.Source)),
            new GemData(DvDestination, GemValue.Ascii(substrate.Destination)));
    }

    private void ReportLocation(string code, string location, string substrateId)
    {
        _gem?.Report(this, code,
            new GemData(DvLocationId, GemValue.Ascii(location)),
            new GemData(DvLocState, code == LocationOccupied ? Occupied : Unoccupied),
            new GemData(DvLocSubstId, GemValue.Ascii(substrateId)));
    }

    #endregion

    #region E39 对象

    private IReadOnlyList<string> SubstrateIds()
    {
        lock (_gate)
        {
            return _substrates.Values.Select(substrate => substrate.SubstId).ToList();
        }
    }

    private bool TryGetSubstrate(string id, string attribute, out SecsItem value)
    {
        lock (_gate)
        {
            value = SecsItem.L();
            var substrate = _substrates.Values.FirstOrDefault(item => string.Equals(item.SubstId, id, StringComparison.OrdinalIgnoreCase));
            if (substrate is null)
            {
                return false;
            }

            value = attribute switch
            {
                "ObjType" => SecsItem.A("Substrate"),
                "ObjID" => SecsItem.A(GemValue.Ascii(substrate.SubstId)),
                "LotID" => SecsItem.A(GemValue.Ascii(substrate.LotId)),
                "MaterialStatus" => SecsItem.U1(),
                "SubstDestination" => SecsItem.A(GemValue.Ascii(substrate.Destination)),
                "SubstHistory" => SecsItem.L(substrate.History.Select(visit => SecsItem.L(
                    SecsItem.A(GemValue.Ascii(visit.Location)),
                    SecsItem.A(GemValue.Time(visit.TimeIn, 1)),
                    SecsItem.A(visit.TimeOut is null ? string.Empty : GemValue.Time(visit.TimeOut.Value, 1))))),
                "SubstLocID" => SecsItem.A(GemValue.Ascii(substrate.Location)),
                "SubstProcState" => SecsItem.U1(substrate.ProcState),
                "SubstSource" => SecsItem.A(GemValue.Ascii(substrate.Source)),
                "SubstState" => SecsItem.U1(substrate.State),
                "SubstType" => SecsItem.U1(0),
                "SubstUsage" => SecsItem.U1(),
                _ => SecsItem.L(),
            };
            return true;
        }
    }

    /// <summary>全部片位号（账本注册过的每个位置的每个槽）。</summary>
    private IReadOnlyList<string> LocationIds()
    {
        var ledger = _ledger;
        if (ledger is null)
        {
            return [];
        }

        return ledger.Locations.SelectMany(location => Enumerable.Range(1, location.SlotCount)
            .Select(slot => location.SlotCount == 1 ? location.Module : $"{location.Module}.{slot.ToString("00", CultureInfo.InvariantCulture)}"))
            .ToList();
    }

    private bool TryGetLocation(string id, string attribute, out SecsItem value)
    {
        value = SecsItem.L();
        var ledger = _ledger;
        if (ledger is null)
        {
            return false;
        }

        foreach (var (module, slots) in ledger.Locations)
        {
            for (int slot = 1; slot <= slots; slot++)
            {
                string location = slots == 1 ? module : $"{module}.{slot.ToString("00", CultureInfo.InvariantCulture)}";
                if (!string.Equals(location, id.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var wafer = ledger.Get(module, slot);
                value = attribute switch
                {
                    "ObjType" => SecsItem.A("SubstLoc"),
                    "ObjID" => SecsItem.A(GemValue.Ascii(location)),
                    "SubstID" => SecsItem.A(GemValue.Ascii(wafer?.WaferId ?? string.Empty)),
                    "SubstLocState" => SecsItem.U1(wafer is null ? Unoccupied : Occupied),
                    _ => SecsItem.L(),
                };
                return true;
            }
        }

        return false;
    }

    /// <summary>E39 对象类型 Substrate。</summary>
    private sealed class SubstrateType : IE39ObjectType
    {
        private readonly E90Component _owner;

        public SubstrateType(E90Component owner)
        {
            _owner = owner;
        }

        public string TypeName => "Substrate";

        public IReadOnlyList<string> AttributeNames { get; } =
        [
            "ObjType", "ObjID", "LotID", "MaterialStatus", "SubstDestination", "SubstHistory", "SubstLocID", "SubstProcState",
            "SubstSource", "SubstState", "SubstType", "SubstUsage",
        ];

        public IReadOnlyList<string> ObjectIds()
        {
            return _owner.SubstrateIds();
        }

        public bool TryGetAttribute(string objectId, string attribute, out SecsItem value)
        {
            return _owner.TryGetSubstrate(objectId, attribute, out value);
        }
    }

    /// <summary>E39 对象类型 SubstLoc。</summary>
    private sealed class LocationType : IE39ObjectType
    {
        private readonly E90Component _owner;

        public LocationType(E90Component owner)
        {
            _owner = owner;
        }

        public string TypeName => "SubstLoc";

        public IReadOnlyList<string> AttributeNames { get; } = ["ObjType", "ObjID", "SubstID", "SubstLocState"];

        public IReadOnlyList<string> ObjectIds()
        {
            return _owner.LocationIds();
        }

        public bool TryGetAttribute(string objectId, string attribute, out SecsItem value)
        {
            return _owner.TryGetLocation(objectId, attribute, out value);
        }
    }

    #endregion
}
