namespace xyz.Modules;

/// <summary>
/// E84 交接组件契约（本端口是被动方）：LoadPort 只依赖本接口，信号接在哪、怎么读写由实现决定。
///
/// 时序照 SEMI E84 与 CTC 的 E84Passiver：端口可交接时亮 HO_AVBL；搬运车 CS_0+VALID 选中本端口后，
/// 按 L_REQ/U_REQ → TR_REQ → READY → BUSY → 载具放上/取走 → COMPT → 撤信号 走完一次交接；
/// 任一段（TP1–TP5）超时就把输出全灭并锁住，等人工 Retry 或 Complete。
///
/// 组件不认识端口，由 LoadPort 驱动（和 RFID 一样）：端口每个扫描周期调 <see cref="Step"/>，
/// 给出这一拍的许可和载具在位；交接进展由 Step 返回，端口转给 EAP。
/// </summary>
public interface IE84
{
    /// <summary>
    /// 打开：输出全灭、回初始；端口 Open 时调用。
    /// </summary>
    bool Open();

    /// <summary>
    /// 推一拍：读输入、走一步、写输出；在端口扫描线程上调。
    /// 返回这一拍（含上一拍之后 Retry/Complete）产生的交接进展，没有为空。
    /// </summary>
    IReadOnlyList<E84Report> Step(E84Permit permit, bool carrierPlaced);

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
    /// 放弃这次交接重来
    /// </summary>
    void Retry();

    /// <summary>
    /// 超时后人工确认这次交接其实已经完成——送盒时载具确实已放上，取盒时确实已取走——
    /// 按完成收尾并上报（CTC 的 E84Complete）。carrierPlaced 由调用方给出当前载具在位；
    /// 没锁住或载具位置对不上返回 false，保持锁住。
    /// </summary>
    bool Complete(bool carrierPlaced);
}
