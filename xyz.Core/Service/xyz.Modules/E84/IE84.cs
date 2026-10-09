using xyz.Components.Enums;

namespace xyz.Modules;


public interface IE84
{
    /// <summary>
    /// 装没装（SC，改了重启生效）：False = 本机没接搬运车，端口当没有 E84——不打开、不扫、不读写 IO。
    /// 跟 E84Enabled（EC，现场在线开关交接）不是一回事。
    /// </summary>
    bool IsEnable { get; }

    /// <summary>
    /// 推一拍：读输入、走一步、写输出；在端口扫描线程上调。端口只给事实，能不能交接、往哪个方向由 E84 自己判：
    /// autoMode = Auto/Manual，transferState = 端口的搬运状态（接了 EAP 都以 EAP 为准），carrierPlaced = 载具到了没有。
    /// 返回这一拍（含上一拍之后 Retry/Complete）产生的交接进展，没有为空。
    /// </summary>
    IReadOnlyList<E84Report> Step(bool autoMode, LoadPortTransferState transferState, bool carrierPlaced);

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
