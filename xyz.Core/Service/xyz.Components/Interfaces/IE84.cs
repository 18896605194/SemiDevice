using xyz.Components.Enums;
using xyz.Components.Models;

namespace xyz.Components.Interfaces;


public interface IE84
{
    bool IsEnable { get; }

    /// <summary>
    /// 推进每一步
    /// </summary>
    /// <param name="autoMode"></param>
    /// <param name="transferState"></param>
    /// <param name="carrierPlaced"></param>
    /// <returns></returns>
    IReadOnlyList<E84Report> Step(bool autoMode, LoadPortTransferState transferState, bool carrierPlaced);

    /// <summary>
    /// 是否启用 E84
    /// </summary>
    bool E84Enabled { get; }

    /// <summary>
    /// 当前走到哪一步。
    /// </summary>
    E84State State { get; }

    /// <summary>
    /// 交接走到一半（给了 READY 以后、交接完成以前），或者超时锁着等人处理：搬运车可能还挂着载具，端口这时别动机构（Load 联锁看它）。
    /// </summary>
    bool IsHandoffRunning { get; }

    /// <summary>
    /// 最近一次读到的输入。
    /// </summary>
    E84Inputs Inputs { get; }

    /// <summary>
    /// 当前输出。
    /// </summary>
    E84Outputs Outputs { get; }

    /// <summary>
    /// 超时锁住时是哪一段；没锁住为 null。
    /// </summary>
    E84Timer? TimedOutTimer { get; }

    /// <summary>
    /// 放弃这次交接重来
    /// </summary>
    void Retry();

    bool Complete(bool carrierPlaced);
}
