using xyz.Common.Log;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Components.Components;

/// <summary>
/// 同腔交换：占住站点一轮，取片手先取走做完的旧片，换段准备后放片手放进新片，最后整轮收尾；中途不把站点放给别人
/// </summary>
public sealed class SwapRoutine : TransferExecution
{
    private readonly ITransferStation _station;
    private readonly int _slot;
    private readonly int _pickArm;
    private readonly int _placeArm;
    private readonly TransferContext _context;

    /// <summary>
    /// 换出来的旧片：站点 → 取片手（做完停在手上）
    /// </summary>
    private readonly TransferWafer _picked;

    /// <summary>
    /// 放进去的新片：放片手 → 站点
    /// </summary>
    private readonly TransferWafer _placed;

    private readonly IReadOnlyList<TransferWafer> _wafers;

    /// <summary>
    /// 占着站点的环（准备过、还没收尾或撤掉）
    /// </summary>
    private bool _holdingStation;

    /// <summary>
    /// 建交换：参数在搬运管理受理时已经查过；owner 是日志记在谁名下（搬运管理）
    /// </summary>
    public SwapRoutine(ILogSource owner, SwapRequest request, ITransferRobot robot, ITransferStation station,
        string pickWaferName, string placeWaferName, int stationWaitTimeout)
        : base(owner, $"Swap {station.Name}.{request.Slot:00} Arm{request.PickArm}↔Arm{request.PlaceArm}",
            request.Origin, robot, stationWaitTimeout)
    {
        _station = station;
        _slot = request.Slot;
        _pickArm = request.PickArm;
        _placeArm = request.PlaceArm;
        _context = TransferContext.ForPickThenPlace(robot.Name, request.PickArm, request.Slot, request.PlaceArm, request.Slot);

        _picked = new TransferWafer(station.Name, request.Slot, robot.Name, request.PickArm, request.PickArm, true)
        {
            Id = request.PickWafer,
            Name = pickWaferName,
            Owner = request.PickOwner,
        };
        _placed = new TransferWafer(robot.Name, request.PlaceArm, station.Name, request.Slot, request.PlaceArm, false)
        {
            Id = request.PlaceWafer,
            Name = placeWaferName,
            Owner = request.PlaceOwner,
        };
        _wafers = [_picked, _placed];

        AddGrabStep(station, _context, () => _holdingStation = true);
        AddPrepareStep($"{station.Name} 准备二", station, () => station.PrepareTransfer2(_context), SecondPreparePhase);
        AddMotionSteps($"Arm{_pickArm} 取旧片", station, _context, _pickArm, _slot, true);
        Do("取完记进度", FinishPick);
        AddPrepareStep($"{station.Name} 换段准备", station, () => station.PrepareNextLeg(_context), NextLegPhase);
        AddMotionSteps($"Arm{_placeArm} 放新片", station, _context, _placeArm, _slot, false);
        Do("放完收尾", FinishPlace);
    }

    /// <summary>
    /// 在哪个站点交换
    /// </summary>
    public ITransferStation Station => _station;

    /// <summary>
    /// 站点的哪个槽
    /// </summary>
    public int Slot => _slot;

    /// <summary>
    /// 取旧片的手
    /// </summary>
    public int PickArm => _pickArm;

    /// <summary>
    /// 放新片的手
    /// </summary>
    public int PlaceArm => _placeArm;

    public override IReadOnlyList<TransferWafer> Wafers => _wafers;

    protected override bool IsHoldingStation => _holdingStation;

    protected override TransferClaims CreateClaims()
    {
        return new TransferClaims(
            [
                TransferManager.SlotKey(_station.Name, _slot),
                TransferManager.SlotKey(Robot.Name, _pickArm),
                TransferManager.SlotKey(Robot.Name, _placeArm),
            ],
            [TransferManager.ArmKey(Robot.Name, _pickArm), TransferManager.ArmKey(Robot.Name, _placeArm)],
            [_picked.Id, _placed.Id],
            [_station.Name]);
    }

    /// <summary>
    /// 旧片取完（机械手记完账）：不收尾，接着换段准备；没取成不会走到这儿，站点停在 Transferring 等人工
    /// </summary>
    private HandleResult FinishPick()
    {
        _picked.Progress = TransferWaferProgress.Picked;
        return HandleResult.Success();
    }

    /// <summary>
    /// 新片放完（机械手记完账）：整轮收尾、放掉站点；没放成旧片照样算取出来了，整条挂起等人工
    /// </summary>
    private HandleResult FinishPlace()
    {
        _placed.Progress = TransferWaferProgress.Placed;
        if (!_station.TransferComplete(_context))
        {
            return HandleResult.Fail(ErrorCodes.TransferStepRejected, _station.Name, "TransferComplete");
        }

        _holdingStation = false;
        return HandleResult.Success();
    }

    /// <summary>
    /// 没动过手时撤掉站点准备；动过手的留着等人工
    /// </summary>
    protected override void ReleaseStations()
    {
        if (MotionStarted)
        {
            return;
        }

        if (_holdingStation && TryCancelStation(_station, _context))
        {
            _holdingStation = false;
        }
    }
}
