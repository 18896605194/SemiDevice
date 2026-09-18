namespace xyz.Modules;

/// <summary>
/// E84 交接组件契约（本端口是被动方）：LoadPort 只依赖本接口，信号接在哪、怎么读写由实现决定。
///
/// 时序照 SEMI E84 与 CTC 的 E84Passiver：端口可交接时亮 HO_AVBL；搬运车 CS_0+VALID 选中本端口后，
/// 按 L_REQ/U_REQ → TR_REQ → READY → BUSY → 载具放上/取走 → COMPT → 撤信号 走完一次交接；
/// 任一段（TP1–TP5）超时就把输出全灭并锁住，等人工 Retry 或 Complete。
/// </summary>
public interface IE84
{
    /// <summary>
    /// 挂上所属端口，输出全灭回初始；端口 Open 时调用。
    /// 之后每个扫描周期从端口取现况、把交接进展报给端口。
    /// </summary>
    void Attach(IE84Host host);

    /// <summary>
    /// 是否启用 E84（EC）。关着时输出全灭，不理搬运车。
    /// </summary>
    bool E84Enabled { get; }

    /// <summary>
    /// 当前走到哪一步。
    /// </summary>
    E84State State { get; }

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
    /// 放弃这次交接重来：清锁存、输出全灭，下一拍重新等搬运车（CTC 的 E84Retry）。
    /// 交接进行中被放弃按中止上报。
    /// </summary>
    void Retry();

    /// <summary>
    /// 超时后人工确认这次交接其实已经完成——送盒时载具确实已放上，取盒时确实已取走——
    /// 按完成收尾并上报（CTC 的 E84Complete）。没锁住或载具位置对不上返回 false，保持锁住。
    /// </summary>
    bool Complete();
}

/// <summary>
/// E84 组件向所属端口要的现况和上报口，由 LoadPort 实现。
/// 都在端口的扫描线程上调（E84 组件随端口扫描）；上报由端口转给 EAP，不在这里等 EAP。
/// </summary>
public interface IE84Host
{
    /// <summary>
    /// 端口名。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Access Mode 是否 Auto；接了 EAP 以 EAP 为准
    /// </summary>
    bool IsAutoAccessMode { get; }

    /// <summary>
    /// 端口搬运状态：决定这次是送盒还是取盒、能不能交接；接了 EAP 以 EAP 为准，没接由端口按本地状态判断。
    /// </summary>
    LoadPortTransferState TransferState { get; }

    /// <summary>
    /// 载具在位。
    /// </summary>
    bool IsCarrierPlaced { get; }

    /// <summary>
    /// 交接开始（READY 已给出）；isLoad = true 为送盒进来，false 为把盒取走。
    /// </summary>
    void HandoffStarted(bool isLoad);

    /// <summary>
    /// 交接正常结束，或超时后人工 Complete 确认已完成。
    /// </summary>
    void HandoffCompleted(bool isLoad);

    /// <summary>
    /// 某一段握手超时，本次交接中止。
    /// </summary>
    void HandoffTimedOut(bool isLoad, E84Timer timer);

    /// <summary>
    /// 交接进行中被打断（切 Manual、下线、光幕被挡、人工 Retry 等）。
    /// </summary>
    void HandoffAborted(bool isLoad, string reason);

    /// <summary>
    /// HO_AVBL 变了。
    /// </summary>
    void AvailabilityChanged(bool available);
}
