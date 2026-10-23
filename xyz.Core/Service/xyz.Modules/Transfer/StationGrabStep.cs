using System.Globalization;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 搬运的一步：抢站点（发准备一）。站点不在待命（给 null）不算失败，下一拍接着抢，等到上限才判负；抢到了等准备一做完
/// </summary>
internal sealed class StationGrabStep : RoutineStep
{
    private readonly ITransferStation _station;
    private readonly TransferContext _context;
    private readonly int _timeoutMs;

    /// <summary>
    /// 抢到了：搬运记下占着这个站点
    /// </summary>
    private readonly Action _grabbed;

    /// <summary>
    /// 站点的准备一；还没抢到为 null
    /// </summary>
    private ModuleRoutine? _prepare;

    public StationGrabStep(string name, ITransferStation station, TransferContext context, int timeoutMs, Action grabbed) : base(name)
    {
        _station = station;
        _context = context;
        _timeoutMs = timeoutMs;
        _grabbed = grabbed;
    }

    /// <summary>
    /// 还没抢到时等站点空出来，抢到了等它准备
    /// </summary>
    public override string Waiting
    {
        get
        {
            var prepare = _prepare;
            if (prepare is null)
            {
                return $"{_station.Name} 空出来";
            }

            if (prepare.IsSettled)
            {
                return string.Empty;
            }

            return $"{_station.Name} 准备一";
        }
    }

    protected internal override bool IsSettled
    {
        get
        {
            var prepare = _prepare;
            return prepare is null || prepare.IsSettled;
        }
    }

    protected internal override void Run()
    {
    }

    protected internal override bool Check()
    {
        var prepare = _prepare;
        if (prepare is null)
        {
            prepare = _station.PrepareTransfer(_context);
            if (prepare is null)
            {
                // 上限 0 就是不等：抢一次没抢到就判负
                if (ElapsedMs >= _timeoutMs)
                {
                    string timeout = _timeoutMs.ToString(CultureInfo.InvariantCulture);
                    Fail(ErrorCodes.StationBusy, $"{_station.Name} 等不到可服务（{timeout} ms）", _station.Name, timeout);
                }

                return false;
            }

            _prepare = prepare;
            _grabbed();
        }

        if (!prepare.IsSettled)
        {
            return false;
        }

        if (!prepare.IsSuccess)
        {
            Fail(ErrorCodes.StationPrepareFailed, $"{_station.Name} 第 1 步准备失败：{prepare.Reason}", _station.Name, "1");
            return false;
        }

        return true;
    }

    protected internal override void OnAborted(string reason)
    {
        _prepare?.AbortByHost(reason);
    }
}
