using ProtoBuf.Grpc;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Database.Wafers;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Wafers;


public class WaferLedgerService : BaseService, IWaferLedgerService
{
    private readonly ILedgerManager? _ledger;

    public WaferLedgerService()
    {
        _ledger = IocHelper.GetService<ILedgerManager>();
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

    public Task<RpcResponse> MoveAsync(RpcRequest request, CallContext context = default)
    {
        if (_ledger is null || !_ledger.IsEnable)
        {
            return Fail(ErrorCodes.WaferLedgerDisabled);
        }

        if (!request.TryGet<string>(RpcParams.FromModule, out var fromModule)
            || !request.TryGet<int>(RpcParams.FromSlot, out var fromSlot)
            || !request.TryGet<string>(RpcParams.ToModule, out var toModule)
            || !request.TryGet<int>(RpcParams.ToSlot, out var toSlot))
        {
            return InvalidRequest();
        }

        string reason = request.Get(RpcParams.Reason, string.Empty);
        string from = fromModule.Trim(), to = toModule.Trim();
        var result = _ledger.ManualMove(from, fromSlot, to, toSlot, RpcOperator.Of(context), reason);
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
                return SlotOutOfRange(_ledger, from, fromSlot, to, toSlot);
            case WaferAdjustResult.NoWafer:
                return Fail(ErrorCodes.WaferNoWafer, from, fromSlot.ToString());
            case WaferAdjustResult.SameSlot:
                return Fail(ErrorCodes.WaferSameSlot);
            default:
                return Fail(ErrorCodes.WaferSlotOccupied, to, toSlot.ToString(), _ledger.Get(to, toSlot)?.WaferId ?? string.Empty);
        }
    }

    public Task<RpcResponse> DeleteAsync(RpcRequest request, CallContext context = default)
    {
        if (_ledger is null || !_ledger.IsEnable)
        {
            return Fail(ErrorCodes.WaferLedgerDisabled);
        }

        if (!request.TryGet<string>(RpcParams.Module, out var module)
            || !request.TryGet<int>(RpcParams.Slot, out var slot))
        {
            return InvalidRequest();
        }

        string reason = request.Get(RpcParams.Reason, string.Empty);
        module = module.Trim();
        var result = _ledger.ManualDelete(module, slot, RpcOperator.Of(context), reason);
        switch (result)
        {
            case WaferAdjustResult.Ok:
                return Task.FromResult(RpcResponse.Ok());
            case WaferAdjustResult.Disabled:
                return Fail(ErrorCodes.WaferLedgerDisabled);
            case WaferAdjustResult.LocationNotFound:
                return Fail(ErrorCodes.WaferLocationNotFound, module);
            case WaferAdjustResult.SlotOutOfRange:
                return Fail(ErrorCodes.WaferSlotOutOfRange, module, slot.ToString(), _ledger.GetSlots(module).Count.ToString());
            default:
                return Fail(ErrorCodes.WaferNoWafer, module, slot.ToString());
        }
    }

    public Task<RpcResponse> CreateAsync(RpcRequest request, CallContext context = default)
    {
        if (_ledger is null || !_ledger.IsEnable)
        {
            return Fail(ErrorCodes.WaferLedgerDisabled);
        }

        if (!request.TryGet<string>(RpcParams.Module, out var module)
            || !request.TryGet<int>(RpcParams.Slot, out var slot)
            || !request.TryGet<string>(RpcParams.WaferId, out var waferId))
        {
            return InvalidRequest();
        }

        string reason = request.Get(RpcParams.Reason, string.Empty);
        module = module.Trim();
        waferId = waferId.Trim();
        var result = _ledger.ManualCreate(module, slot, waferId, RpcOperator.Of(context), reason);
        switch (result)
        {
            case WaferAdjustResult.Ok:
                return Task.FromResult(RpcResponse.Ok());
            case WaferAdjustResult.Disabled:
                return Fail(ErrorCodes.WaferLedgerDisabled);
            case WaferAdjustResult.LocationNotFound:
                return Fail(ErrorCodes.WaferLocationNotFound, module);
            case WaferAdjustResult.SlotOutOfRange:
                return Fail(ErrorCodes.WaferSlotOutOfRange, module, slot.ToString(), _ledger.GetSlots(module).Count.ToString());
            case WaferAdjustResult.SlotOccupied:
                return Fail(ErrorCodes.WaferSlotOccupied, module, slot.ToString(), _ledger.Get(module, slot)?.WaferId ?? string.Empty);
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
            foreach (string station in robot.TeachPoints.Keys)
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
    /// 片在哪一类模块上：照抄模块自己报的，不判断具体类型；名字认不出模块的算其他
    /// </summary>
    private static StationKind KindOf(IModule? module)
    {
        if (module is null)
        {
            return StationKind.Other;
        }

        return module.Kind;
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

    /// <summary>
    /// 槽号超范围：说清楚是源还是目标的哪一个槽超了。
    /// </summary>
    private static Task<RpcResponse> SlotOutOfRange(ILedgerManager ledger, string from, int fromSlot, string to, int toSlot)
    {
        int fromCount = ledger.GetSlots(from).Count;
        return fromSlot < 1 || fromSlot > fromCount
            ? Fail(ErrorCodes.WaferSlotOutOfRange, from, fromSlot.ToString(), fromCount.ToString())
            : Fail(ErrorCodes.WaferSlotOutOfRange, to, toSlot.ToString(), ledger.GetSlots(to).Count.ToString());
    }
}
