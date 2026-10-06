using System.Globalization;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Modules.Enums;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 算全部回片的计划（只看账和设备状态，不动设备）：机械手手上的、腔体和别的站点上的每一片，回它的来源 LoadPort 同号槽。
/// 回不去的写原因：片还在跑着的 Job 里、不知道从哪来、来源 LoadPort 上没有能放片的载具、载具换过了、
/// 来源槽上有片了、片或槽被出错的搬运单锁着、没有机械手两边都到得了。LoadPort 上的片不动。
/// </summary>
internal static class ReturnPlanner
{
    public static ReturnPlan Plan(TransferManager transfers, WaferManager ledger)
    {
        var moves = new List<ReturnMove>();
        var skipped = new List<ReturnSkip>();
        var view = transfers.GetView();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Consider(WaferInfo wafer, string source, int slot, IRobot? holder)
        {
            var move = PlanOne(transfers, ledger, view, claimed, wafer, source, slot, holder, out var code, out var args);
            if (move is not null)
            {
                moves.Add(move);
            }
            else
            {
                skipped.Add(new ReturnSkip(wafer.Id, wafer.WaferId, source, slot, holder is not null, code, args));
            }
        }

        // 机械手手上的先回：手空出来，腔体里的才有手去取
        foreach (var robot in transfers.Robots)
        {
            var arms = ledger.GetSlots(robot.Name);
            for (int arm = 1; arm <= arms.Count; arm++)
            {
                var wafer = arms[arm - 1];
                if (wafer is not null)
                {
                    Consider(wafer, robot.Name, arm, robot);
                }
            }
        }

        // 再是机内别的站点（腔体、对中台……）；LoadPort 上的片本来就在载具里，不动
        foreach (var station in transfers.Stations)
        {
            if (station is ILoadPort)
            {
                continue;
            }

            var slots = ledger.GetSlots(station.Name);
            for (int slot = 1; slot <= slots.Count; slot++)
            {
                var wafer = slots[slot - 1];
                if (wafer is not null)
                {
                    Consider(wafer, station.Name, slot, null);
                }
            }
        }

        return new ReturnPlan(moves, skipped);
    }

    private static ReturnMove? PlanOne(TransferManager transfers, WaferManager ledger, TransferView view, ISet<string> claimed,
        WaferInfo wafer, string source, int sourceSlot, IRobot? holder, out string code, out IReadOnlyList<string> args)
    {
        string? owner = transfers.Ownership?.OwnerOf(wafer.Id);
        if (owner is not null)
        {
            return Skip(ErrorCodes.TransferWaferOwned, out code, out args, wafer.WaferId, owner);
        }

        string? port = wafer.SourceLoadPort;
        int slot = wafer.SourceSlot;
        if (string.IsNullOrEmpty(port) || slot < 1)
        {
            return Skip(ErrorCodes.TransferReturnNoSource, out code, out args, wafer.WaferId);
        }

        string slotText = slot.ToString(CultureInfo.InvariantCulture);
        if (!transfers.TryGetStation(port, out var target) || target is not ILoadPort loadPort)
        {
            return Skip(ErrorCodes.TransferStationNotFound, out code, out args, port);
        }

        if (!IsReadyForReturn(loadPort))
        {
            return Skip(ErrorCodes.TransferReturnPortNotReady, out code, out args, wafer.WaferId, port);
        }

        // 载具换过了：片记着的载具号跟 LoadPort 上现在的对不上，不能往别人的载具里放（有一边没读到号就不比）
        string? expected = wafer.CarrierId;
        string? actual = loadPort.CarrierId;
        if (!string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(actual)
            && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            return Skip(ErrorCodes.TransferReturnCarrierChanged, out code, out args, wafer.WaferId, port, expected, actual);
        }

        if (slot > target.SlotCount)
        {
            return Skip(ErrorCodes.TransferSlotOutOfRange, out code, out args,
                port, slotText, target.SlotCount.ToString(CultureInfo.InvariantCulture));
        }

        var occupant = ledger.Get(port, slot);
        if (occupant is not null)
        {
            return Skip(ErrorCodes.WaferSlotOccupied, out code, out args, port, slotText, occupant.WaferId);
        }

        string sourceSlotText = sourceSlot.ToString(CultureInfo.InvariantCulture);
        if (view.IsWaferLocked(wafer.Id) || view.IsSlotLocked(source, sourceSlot))
        {
            return Skip(ErrorCodes.TransferSlotLocked, out code, out args, source, sourceSlotText);
        }

        if (view.IsSlotLocked(port, slot) || !claimed.Add(TransferManager.SlotKey(port, slot)))
        {
            return Skip(ErrorCodes.TransferSlotLocked, out code, out args, port, slotText);
        }

        bool reachable = holder is not null
            ? holder.TryGetStation(port, out _)
            : transfers.Robots.Any(robot => robot.TryGetStation(source, out _) && robot.TryGetStation(port, out _));
        if (!reachable)
        {
            claimed.Remove(TransferManager.SlotKey(port, slot));
            return Skip(ErrorCodes.TransferNoRobot, out code, out args, source, port);
        }

        code = string.Empty;
        args = [];
        return new ReturnMove(wafer.Id, wafer.WaferId, source, sourceSlot, holder is not null, port, slot);
    }

    /// <summary>来源 LoadPort 能不能放片：载具在位，Load 过（或正在跟机械手交互）。跟 Job 判"载具能取片"一个标准。</summary>
    private static bool IsReadyForReturn(ILoadPort port)
    {
        if (!port.IsCarrierArrived)
        {
            return false;
        }

        int state = port.State;
        return state == LoadPortState.Loaded
            || (state >= TransferModuleState.PreTransfer && state <= TransferModuleState.TransferComplete);
    }

    private static ReturnMove? Skip(string code, out string skipCode, out IReadOnlyList<string> skipArgs, params string[] args)
    {
        skipCode = code;
        skipArgs = args;
        return null;
    }
}
