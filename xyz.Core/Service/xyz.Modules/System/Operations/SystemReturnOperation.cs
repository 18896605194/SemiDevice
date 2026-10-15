using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 全部回片：机内（机械手手上、腔体和别的站点上）每一片回它的来源 LoadPort 原槽，LoadPort 上的片本来就在载具里，不动。
/// 第一拍照晶圆账排好先后——机械手手上的先回（手空出来，腔体里的才有手去取），回不去的记日志跳过：
/// 片还归没结束的 Job、不知道从哪个 LoadPort 来、那个 LoadPort 没有 Load 着的载具、载具换过了、原槽上有片了。
/// 之后每拍把能下的单交给搬运管理（人工恢复口径），机械手、手、槽一时被占着的下一拍再试，别的原因被拒记成回不去；
/// 没有在搬的、这一拍也一张都下不进去，剩下的再等也下不进去，都记成回不去。
/// 全回去了算做成；有回不去的算没做成（整机照样回空闲，原因都在日志里，要人工处理）。
/// </summary>
internal sealed class SystemReturnOperation : ModuleOperation<SystemReturnOperation.ReturnStep>
{
    internal enum ReturnStep
    {
        Plan,
        Run,
    }

    /// <summary>日志记在系统模块名下。</summary>
    private readonly string _logModule;

    private readonly List<TransferRequest> _pending = [];
    private readonly List<TransferRoutine> _running = [];
    private readonly Dictionary<TransferRequest, string> _lastBusy = [];
    private int _notReturned;

    public SystemReturnOperation(string logModule) : base("ReturnAll", ReturnStep.Plan)
    {
        _logModule = logModule;
    }

    protected override void OnScan()
    {
        // 入口查过装没装，这里兜个底
        var transfers = TransferManager.Current;
        if (transfers is null)
        {
            Fail(ErrorCodes.TransferNotInstalled, "没装搬运管理");
            return;
        }

        var ledger = WaferManagerComponent.Current;
        if (ledger is null)
        {
            Fail(ErrorCodes.WaferLedgerDisabled, "没装晶圆账");
            return;
        }

        switch (Step)
        {
            case ReturnStep.Plan:
                Plan(transfers, ledger);
                LogHelper.Info(_logModule, $"全部回片：要回 {_pending.Count} 片，回不去 {_notReturned} 片");
                SetStep(ReturnStep.Run);
                break;
            case ReturnStep.Run:
                if (Advance(transfers))
                {
                    Finish();
                }

                break;
        }
    }

    #region 排计划

    private void Plan(TransferManager transfers, WaferManagerComponent ledger)
    {
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var robot in transfers.Robots)
        {
            var arms = ledger.GetSlots(robot.Name);
            for (int arm = 1; arm <= arms.Count; arm++)
            {
                var wafer = arms[arm - 1];
                if (wafer is not null)
                {
                    Consider(transfers, ledger, claimed, wafer, robot.Name, arm);
                }
            }
        }

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
                    Consider(transfers, ledger, claimed, wafer, station.Name, slot);
                }
            }
        }
    }

    /// <summary>这一片回不回得去：回得去排进计划，回不去记日志。</summary>
    private void Consider(TransferManager transfers, WaferManagerComponent ledger, HashSet<string> claimed,
        WaferInfo wafer, string source, int sourceSlot)
    {
        string? reason = Check(transfers, ledger, claimed, wafer);
        if (reason is not null)
        {
            _notReturned++;
            LogHelper.Warn(_logModule, $"全部回片：{wafer.WaferId}（{source} {sourceSlot}）回不去：{reason}");
            return;
        }

        _pending.Add(new TransferRequest
        {
            Origin = TransferOrigin.Recovery,
            WaferId = wafer.Id,
            Source = source,
            SourceSlot = sourceSlot,
            Target = wafer.SourceLoadPort!,
            TargetSlot = wafer.SourceSlot,
        });
    }

    /// <summary>回不去的原因；回得去返回 null，并占住原槽（两片不会排进同一个槽）。</summary>
    private static string? Check(TransferManager transfers, WaferManagerComponent ledger, HashSet<string> claimed, WaferInfo wafer)
    {
        string? owner = JobManager.Current?.OwnerOf(wafer.Id);
        if (owner is not null)
        {
            return $"还归 Job {owner}，由 Job 收场";
        }

        string? port = wafer.SourceLoadPort;
        int slot = wafer.SourceSlot;
        if (string.IsNullOrEmpty(port) || slot < 1)
        {
            return "不知道从哪个 LoadPort 来（不是在 LoadPort 上建的片）";
        }

        if (!transfers.TryGetStation(port, out var target) || target is not ILoadPort loadPort)
        {
            return $"来源 {port} 不是装着的 LoadPort";
        }

        var carrier = loadPort._carrier;
        if (!carrier.IsArrived || !loadPort.IsLoaded)
        {
            return $"{port} 上没有 Load 着的载具";
        }

        // 载具换过了：片记着的载具号跟 LoadPort 上现在的对不上，不能往别人的载具里放（有一边没读到号就不比）
        string? expected = wafer.CarrierId;
        string? actual = carrier.CarrierId;
        if (!string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(actual)
            && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            return $"{port} 上的载具换过了（片记着 {expected}，现在是 {actual}）";
        }

        if (slot > target.SlotCount)
        {
            return $"原槽 {slot} 超出 {port} 的槽数 {target.SlotCount}";
        }

        var occupant = ledger.Get(port, slot);
        if (occupant is not null || !claimed.Add(TransferManager.SlotKey(port, slot)))
        {
            return $"{port} 原槽 {slot} 上已经有片";
        }

        return null;
    }

    #endregion

    #region 下单、收单

    /// <summary>推一拍：收搬完的、下能下的。都收场了返回 true。</summary>
    private bool Advance(TransferManager transfers)
    {
        foreach (var routine in _running.ToList())
        {
            if (!routine.IsSettled)
            {
                continue;
            }

            _running.Remove(routine);
            if (routine.IsSuccess)
            {
                LogHelper.Info(_logModule, $"全部回片：{routine.WaferName} 已回 {routine.TargetName} {routine.TargetSlot}");
            }
            else
            {
                _notReturned++;
                LogHelper.Warn(_logModule, $"全部回片：{routine.WaferName} 没搬成：{routine.Reason}");
            }
        }

        bool admitted = false;
        foreach (var request in _pending.ToList())
        {
            var started = transfers.Start(request);
            var routine = started.Result;
            if (started.IsSuccess && routine is not null)
            {
                _pending.Remove(request);
                _lastBusy.Remove(request);
                _running.Add(routine);
                admitted = true;
            }
            else if (IsBusy(started.ErrorMessage))
            {
                _lastBusy[request] = Describe(started.ErrorMessage, started.Args);
            }
            else
            {
                _pending.Remove(request);
                _lastBusy.Remove(request);
                GiveUp(request, Describe(started.ErrorMessage, started.Args));
            }
        }

        // 卡住了：没有在搬的、这一拍也一张没下进去
        if (_pending.Count > 0 && _running.Count == 0 && !admitted)
        {
            foreach (var request in _pending)
            {
                GiveUp(request, _lastBusy[request]);
            }

            _pending.Clear();
            _lastBusy.Clear();
        }

        return _pending.Count == 0 && _running.Count == 0;
    }

    /// <summary>一时被占着的：机械手或站点在忙、手没空、槽被别的搬运占着，等前面的做完就能下。</summary>
    private static bool IsBusy(string code)
    {
        return code == ErrorCodes.ActionRejected || code == ErrorCodes.TransferNoArm
            || code == ErrorCodes.TransferArmUnavailable || code == ErrorCodes.TransferSlotLocked;
    }

    private void GiveUp(TransferRequest request, string reason)
    {
        _notReturned++;
        LogHelper.Warn(_logModule, $"全部回片：{request.Source} {request.SourceSlot} 的片回不去：{reason}");
    }

    private static string Describe(string code, IReadOnlyList<string> args)
    {
        return args.Count == 0 ? code : $"{code} [{string.Join(", ", args)}]";
    }

    private void Finish()
    {
        if (_notReturned == 0)
        {
            Complete();
            return;
        }

        Fail(ErrorCodes.SystemReturnIncomplete, $"有 {_notReturned} 片没回去", _notReturned.ToString(CultureInfo.InvariantCulture));
    }

    #endregion
}
