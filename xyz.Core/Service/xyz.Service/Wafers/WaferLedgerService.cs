using ProtoBuf.Grpc;
using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Enums;
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

    public WaferLedgerService(IReadOnlyList<ComponentBase> roots) : base(roots)
    {
    }

    public Task<RpcResponse> GetLedgerAsync(RpcRequest request, CallContext context = default)
    {
        var ledger = WaferManagerComponent.Current;
        var dto = new WaferLedgerDto { IsEnabled = ledger is not null && ledger.IsEnable };
        if (ledger is not null && ledger.IsEnable)
        {
            var modules = Modules().ToLookup(module => module.Name, StringComparer.OrdinalIgnoreCase);
            foreach (string name in LocationNames())
            {
                var slots = ledger.GetSlots(name);
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
        var ledger = WaferManagerComponent.Current;
        if (ledger is null || !ledger.IsEnable)
        {
            return Fail(ErrorCodes.WaferLedgerDisabled);
        }

        string from = request.FromModule.Trim(), to = request.ToModule.Trim();
        var result = ledger.ManualMove(from, request.FromSlot, to, request.ToSlot, OperatorOf(request.Operator), request.Reason);
        return result switch
        {
            WaferAdjustResult.Ok => Task.FromResult(RpcResponse.Ok()),
            WaferAdjustResult.Disabled => Fail(ErrorCodes.WaferLedgerDisabled),
            WaferAdjustResult.LocationNotFound => Fail(ErrorCodes.WaferLocationNotFound, ledger.GetSlots(from).Count == 0 ? from : to),
            WaferAdjustResult.SlotOutOfRange => SlotOutOfRange(ledger, from, request.FromSlot, to, request.ToSlot),
            WaferAdjustResult.NoWafer => Fail(ErrorCodes.WaferNoWafer, from, request.FromSlot.ToString()),
            WaferAdjustResult.SameSlot => Fail(ErrorCodes.WaferSameSlot),
            _ => Fail(ErrorCodes.WaferSlotOccupied, to, request.ToSlot.ToString(), ledger.Get(to, request.ToSlot)?.WaferId ?? string.Empty),
        };
    }

    public Task<RpcResponse> DeleteAsync(WaferDeleteRequest request, CallContext context = default)
    {
        var ledger = WaferManagerComponent.Current;
        if (ledger is null || !ledger.IsEnable)
        {
            return Fail(ErrorCodes.WaferLedgerDisabled);
        }

        string module = request.Module.Trim();
        var result = ledger.ManualDelete(module, request.Slot, OperatorOf(request.Operator), request.Reason);
        return result switch
        {
            WaferAdjustResult.Ok => Task.FromResult(RpcResponse.Ok()),
            WaferAdjustResult.Disabled => Fail(ErrorCodes.WaferLedgerDisabled),
            WaferAdjustResult.LocationNotFound => Fail(ErrorCodes.WaferLocationNotFound, module),
            WaferAdjustResult.SlotOutOfRange => Fail(ErrorCodes.WaferSlotOutOfRange, module, request.Slot.ToString(), ledger.GetSlots(module).Count.ToString()),
            _ => Fail(ErrorCodes.WaferNoWafer, module, request.Slot.ToString()),
        };
    }

    public Task<RpcResponse> CreateAsync(WaferCreateRequest request, CallContext context = default)
    {
        var ledger = WaferManagerComponent.Current;
        if (ledger is null || !ledger.IsEnable)
        {
            return Fail(ErrorCodes.WaferLedgerDisabled);
        }

        string module = request.Module.Trim();
        string waferId = request.WaferId.Trim();
        var result = ledger.ManualCreate(module, request.Slot, waferId, OperatorOf(request.Operator), request.Reason);
        switch (result)
        {
            case WaferAdjustResult.Ok:
                return Task.FromResult(RpcResponse.Ok());
            case WaferAdjustResult.Disabled:
                return Fail(ErrorCodes.WaferLedgerDisabled);
            case WaferAdjustResult.LocationNotFound:
                return Fail(ErrorCodes.WaferLocationNotFound, module);
            case WaferAdjustResult.SlotOutOfRange:
                return Fail(ErrorCodes.WaferSlotOutOfRange, module, request.Slot.ToString(), ledger.GetSlots(module).Count.ToString());
            case WaferAdjustResult.SlotOccupied:
                return Fail(ErrorCodes.WaferSlotOccupied, module, request.Slot.ToString(), ledger.Get(module, request.Slot)?.WaferId ?? string.Empty);
            case WaferAdjustResult.WaferIdRequired:
                return Fail(ErrorCodes.WaferIdRequired);
            default:
                // 片号已经在账上：说出在哪，让人去那边用移动
                var existing = ledger.Find(waferId);
                return Fail(ErrorCodes.WaferDuplicateId, waferId, existing?.Module ?? string.Empty, existing?.Slot.ToString() ?? string.Empty);
        }
    }

    public Task<RpcResponse> GetAdjustmentsAsync(RpcRequest request, CallContext context = default)
    {
        var rows = WaferManagerComponent.Current?.GetRecentAdjustments() ?? [];
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(rows.Select(ToDto).ToList())));
    }

    /// <summary>
    /// 能调整的位置名：机械手在前（按组件树的先后），再按各机械手 Stations 里的先后列站点，同名只列一次。
    /// </summary>
    private IEnumerable<string> LocationNames()
    {
        var robots = Modules().OfType<IRobot>().Cast<BaseModule>().ToList();
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
            foreach (string station in ((IRobot)robot).Stations.Keys)
            {
                if (seen.Add(station))
                {
                    names.Add(station);
                }
            }
        }

        return names;
    }

    /// <summary>
    /// 组件树上全部模块（模块一般挂在根上，这里整棵树走一遍，挂在别处的也找得到）。
    /// </summary>
    private IEnumerable<BaseModule> Modules()
    {
        var pending = new Stack<ComponentBase>(Roots.Reverse());
        while (pending.Count > 0)
        {
            var component = pending.Pop();
            if (component is BaseModule module)
            {
                yield return module;
            }

            for (int index = component.Children.Count - 1; index >= 0; index--)
            {
                pending.Push(component.Children[index]);
            }
        }
    }

    private static WaferLocationDto ToDto(string name, BaseModule? module, IReadOnlyList<WaferInfo?> slots)
    {
        return new WaferLocationDto
        {
            Name = module?.Name ?? name,
            Kind = module switch
            {
                IRobot => WaferLocationKind.Robot,
                BaseLoadPortModule => WaferLocationKind.LoadPort,
                BaseChamberModule => WaferLocationKind.Chamber,
                _ => WaferLocationKind.Other,
            },
            CarrierId = (module as BaseLoadPortModule)?._carrier.CarrierId,
            Slots = WaferLedgerSnapshot.ToSlots(slots),
        };
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
    private static Task<RpcResponse> SlotOutOfRange(WaferManagerComponent ledger, string from, int fromSlot, string to, int toSlot)
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
