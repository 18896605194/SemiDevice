using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 一次全部回片：按计划的先后给每片下恢复单（Recovery），由搬运管理的扫描线程一拍一拍推。
/// 下单被拒分两种：手、槽一时被占着（同一台机械手前一张还没做完、手还拿着片）的下一拍再试；别的（片被人动过、目标槽有片了……）记成回不去。
/// 没有在做的单、这一拍也一张都下不进去，剩下的再等也下不进去，都记成回不去，收场。
/// </summary>
internal sealed class ReturnSession
{
    private readonly List<ReturnMove> _pending;
    private readonly Dictionary<long, (ReturnMove Move, Task<TransferResult> Completion)> _running = new();
    private readonly Dictionary<ReturnMove, (string Code, IReadOnlyList<string> Args)> _lastBusy = new();

    public ReturnSession(IEnumerable<ReturnMove> moves)
    {
        _pending = moves.ToList();
    }

    /// <summary>回到来源槽的片。</summary>
    public List<ReturnMove> Returned { get; } = [];

    /// <summary>没回去的片和原因（下单被拒，或搬的时候出错）。</summary>
    public List<ReturnSkip> NotReturned { get; } = [];

    /// <summary>推一拍：收做完的单、下能下的单。全部收场了返回 true。</summary>
    public bool Advance(TransferManager transfers)
    {
        foreach (var (id, entry) in _running.ToList())
        {
            if (!entry.Completion.IsCompleted)
            {
                continue;
            }

            _running.Remove(id);
            var result = entry.Completion.Result;
            if (result.IsSuccess)
            {
                Returned.Add(entry.Move);
            }
            else
            {
                NotReturned.Add(ReturnSkip.Of(entry.Move, result.Code, result.Args));
            }
        }

        bool admitted = false;
        foreach (var move in _pending.ToList())
        {
            var ticket = transfers.Submit(new TransferRequest
            {
                Origin = TransferOrigin.Recovery,
                WaferId = move.WaferId,
                Source = move.Source,
                SourceSlot = move.SourceSlot,
                Target = move.Target,
                TargetSlot = move.TargetSlot,
            });

            var completion = ticket.Completion;
            if (ticket.Accepted && completion is not null)
            {
                _pending.Remove(move);
                _lastBusy.Remove(move);
                _running[ticket.Id] = (move, completion);
                admitted = true;
            }
            else if (IsBusy(ticket.Code))
            {
                _lastBusy[move] = (ticket.Code, ticket.Args);
            }
            else
            {
                _pending.Remove(move);
                _lastBusy.Remove(move);
                NotReturned.Add(ReturnSkip.Of(move, ticket.Code, ticket.Args));
            }
        }

        // 卡住了：没有在做的单、这一拍也一张没下进去
        if (_pending.Count > 0 && _running.Count == 0 && !admitted)
        {
            foreach (var move in _pending)
            {
                var (code, args) = _lastBusy[move];
                NotReturned.Add(ReturnSkip.Of(move, code, args));
            }

            _pending.Clear();
            _lastBusy.Clear();
        }

        return _pending.Count == 0 && _running.Count == 0;
    }

    /// <summary>一时被占着的：等前面的单做完、手空出来就能下。</summary>
    private static bool IsBusy(string code)
    {
        return code == ErrorCodes.TransferNoArm || code == ErrorCodes.TransferArmUnavailable || code == ErrorCodes.TransferSlotLocked;
    }
}
