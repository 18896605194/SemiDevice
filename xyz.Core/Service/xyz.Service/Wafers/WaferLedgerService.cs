using ProtoBuf.Grpc;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Database.Wafers;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Wafers;

/// <summary>
/// 晶圆账 gRPC 服务（设置 → 账单调整页）：给界面看账，人按实物移账、删账、补账。只改系统账，不让设备做任何动作。
/// 能调整的位置跟着配置走：所有机械手 sc.xml 里 Stations 配的站点合并，再加上机械手自己（手指）；
/// 4 个 LoadPort、2 台机械手、8 个腔体也好，再多 Aligner、Buffer 也好，页面都不用改。
/// </summary>
public class WaferLedgerService : BaseService, IWaferLedgerService
{
    /// <summary>
    /// 客户端没带操作人时记成这个（客户端还没做登录前都会带上当前用户名，这里只是兜底）。
    /// </summary>
    private const string UnknownOperator = "Unknown";

    private readonly IWaferManagerComponent? _ledger;

    public WaferLedgerService()
    {
        _ledger = IocHelper.GetService<IWaferManagerComponent>();
    }

    public Task<RpcResponse> GetLedgerAsync(RpcRequest request, CallContext context = default)
    {
        var dto = new WaferLedgerDto { IsEnabled = _ledger is not null && _ledger.IsEnable };
        if (_ledger is not null && _ledger.IsEnable)
        {
            var modules = IocHelper.GetServices<IModule>().ToLookup(module => module.Name, StringComparer.OrdinalIgnoreCase);
            foreach (string name in LocationNames())
            {
                var slots = _ledger.GetSlots(name);
                if (slots.Count == 0)
                {
                    continue;  // 没登记过槽位的站点放不了片，不列
                }

                dto.Locations.Add(ToDto(name, modules[name].FirstOrDefault(), slots));
            }
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(dto)));
    }

    public Task<RpcResponse> MoveAsync(WaferMoveRequest request, CallContext context = default)
    {
        if (_ledger is null || !_ledger.IsEnable)
        {
            return Fail(ErrorCodes.WaferLedgerDisabled);
        }

        string from = request.FromModule.Trim(), to = request.ToModule.Trim();
        var result = _ledger.ManualMove(from, request.FromSlot, to, request.ToSlot, OperatorOf(request.Operator), request.Reason);
        switch (result)
        {
            case WaferAdjustResult.Ok:
                return Task.FromResult(RpcResponse.Ok());
            case WaferAdjustResult.Disabled:
                return Fail(ErrorCodes.WaferLedgerDisabled);
            case WaferAdjustResult.LocationNotFound:
                // 源没登记就报源，不然是目标没登记
                if (_ledger.GetSlots(from).Count == 0)
                {
                    return Fail(ErrorCodes.WaferLocationNotFound, from);
                }

                return Fail(ErrorCodes.WaferLocationNotFound, to);
            case WaferAdjustResult.SlotOutOfRange:
                return SlotOutOfRange(_ledger, from, request.FromSlot, to, request.ToSlot);
            case WaferAdjustResult.NoWafer:
                return Fail(ErrorCodes.WaferNoWafer, from, request.FromSlot.ToString());
            case WaferAdjustResult.SameSlot:
                return Fail(ErrorCodes.WaferSameSlot);
            default:
                return Fail(ErrorCodes.WaferSlotOccupied, to, request.ToSlot.ToString(), _ledger.Get(to, request.ToSlot)?.WaferId ?? string.Empty);
        }
    }

    public Task<RpcResponse> DeleteAsync(WaferDeleteRequest request, CallContext context = default)
    {
        if (_ledger is null || !_ledger.IsEnable)
        {
            return Fail(ErrorCodes.WaferLedgerDisabled);
        }

        string module = request.Module.Trim();
        var result = _ledger.ManualDelete(module, request.Slot, OperatorOf(request.Operator), request.Reason);
        switch (result)
        {
            case WaferAdjustResult.Ok:
                return Task.FromResult(RpcResponse.Ok());
            case WaferAdjustResult.Disabled:
                return Fail(ErrorCodes.WaferLedgerDisabled);
            case WaferAdjustResult.LocationNotFound:
                return Fail(ErrorCodes.WaferLocationNotFound, module);
            case WaferAdjustResult.SlotOutOfRange:
                return Fail(ErrorCodes.WaferSlotOutOfRange, module, request.Slot.ToString(), _ledger.GetSlots(module).Count.ToString());
            default:
                return Fail(ErrorCodes.WaferNoWafer, module, request.Slot.ToString());
        }
    }

    public Task<RpcResponse> CreateAsync(WaferCreateRequest request, CallContext context = default)
    {
        if (_ledger is null || !_ledger.IsEnable)
        {
            return Fail(ErrorCodes.WaferLedgerDisabled);
        }

        string module = request.Module.Trim();
        string waferId = request.WaferId.Trim();
        var result = _ledger.ManualCreate(module, request.Slot, waferId, OperatorOf(request.Operator), request.Reason);
        switch (result)
        {
            case WaferAdjustResult.Ok:
                return Task.FromResult(RpcResponse.Ok());
            case WaferAdjustResult.Disabled:
                return Fail(ErrorCodes.WaferLedgerDisabled);
            case WaferAdjustResult.LocationNotFound:
                return Fail(ErrorCodes.WaferLocationNotFound, module);
            case WaferAdjustResult.SlotOutOfRange:
                return Fail(ErrorCodes.WaferSlotOutOfRange, module, request.Slot.ToString(), _ledger.GetSlots(module).Count.ToString());
            case WaferAdjustResult.SlotOccupied:
                return Fail(ErrorCodes.WaferSlotOccupied, module, request.Slot.ToString(), _ledger.Get(module, request.Slot)?.WaferId ?? string.Empty);
            case WaferAdjustResult.WaferIdRequired:
                return Fail(ErrorCodes.WaferIdRequired);
            default:
                // 片号已经在账上：说出在哪，让人去那边用移动
                var existing = _ledger.Find(waferId);
                return Fail(ErrorCodes.WaferDuplicateId, waferId, existing?.Module ?? string.Empty, existing?.Slot.ToString() ?? string.Empty);
        }
    }

    public Task<RpcResponse> GetAdjustmentsAsync(RpcRequest request, CallContext context = default)
    {
        var rows = _ledger?.GetRecentAdjustments() ?? [];
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(rows.Select(ToDto).ToList())));
    }

    /// <summary>
    /// 能调整的位置名：机械手在前（按组件树的先后），再按各机械手 Stations 里的先后列站点，同名只列一次。
    /// </summary>
    private static IEnumerable<string> LocationNames()
    {
        var robots = IocHelper.GetServices<ITransferRobot>();
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var robot in robots)
        {
            if (seen.Add(robot.Name))
            {
                names.Add(robot.Name);
            }
        }

        foreach (var robot in robots)
        {
            foreach (string station in robot.Stations.Keys)
            {
                if (seen.Add(station))
                {
                    names.Add(station);
                }
            }
        }

        return names;
    }

    private static WaferLocationDto ToDto(string name, IModule? module, IReadOnlyList<WaferInfo?> slots)
    {
        var dto = new WaferLocationDto { Name = name, Kind = KindOf(module), Slots = WaferLedgerSnapshot.ToSlots(slots) };
        if (module is not null)
        {
            dto.Name = module.Name;
        }

        if (module is ILoadPort port)
        {
            dto.CarrierId = port._carrier.CarrierId;
        }

        return dto;
    }

    /// <summary>
    /// 片在哪种站点上
    /// </summary>
    private static WaferLocationKind KindOf(IModule? module)
    {
        if (module is ITransferRobot)
        {
            return WaferLocationKind.Robot;
        }

        if (module is ILoadPort)
        {
            return WaferLocationKind.LoadPort;
        }

        if (module is IChamber)
        {
            return WaferLocationKind.Chamber;
        }

        return WaferLocationKind.Other;
    }


    private static WaferAdjustmentDto ToDto(WaferAdjustmentEntity row)
    {
        return new WaferAdjustmentDto
        {
            Time = row.OccurredAt,
            Action = row.Action,
            WaferId = row.WaferId,
            FromModule = row.FromModule,
            FromSlot = row.FromSlot,
            ToModule = row.ToModule,
            ToSlot = row.ToSlot,
            Operator = row.Operator,
            Reason = row.Reason,
        };
    }

    private static string OperatorOf(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? UnknownOperator : name.Trim();
    }

    /// <summary>
    /// 槽号超范围：说清楚是源还是目标的哪一个槽超了。
    /// </summary>
    private static Task<RpcResponse> SlotOutOfRange(IWaferManagerComponent ledger, string from, int fromSlot, string to, int toSlot)
    {
        int fromCount = ledger.GetSlots(from).Count;
        return fromSlot < 1 || fromSlot > fromCount
            ? Fail(ErrorCodes.WaferSlotOutOfRange, from, fromSlot.ToString(), fromCount.ToString())
            : Fail(ErrorCodes.WaferSlotOutOfRange, to, toSlot.ToString(), ledger.GetSlots(to).Count.ToString());
    }

    private static Task<RpcResponse> Fail(string code, params string[] args)
    {
        return Task.FromResult(RpcResponse.Fail(code, args));
    }
}
