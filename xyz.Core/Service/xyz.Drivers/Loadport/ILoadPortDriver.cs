namespace xyz.Drivers.Loadport;

/// <summary>
/// LoadPort 驱动契约：模块、操作、指令都只认这个接口，不认具体驱动类。
/// </summary>
public interface ILoadPortDriver
{
    /// <summary>
    /// 设备主动上报（FOUP 放上/取走等），在驱动路由线程回调，订阅方应及时返回。
    /// </summary>
    event Action<LoadPortDeviceEvent>? OnSpontaneousEvent;

    bool IsConnected { get; }

    bool Open();

    /// <summary>
    /// 关连接；在途指令全部作废（旧连接上的回复不会再来了，等的人别一直等）。
    /// </summary>
    void Close();

    /// <summary>
    /// 受理一条指令并下发；未连接或同名指令在途时返回 false。一般由指令自己的 Execute 调。
    /// </summary>
    bool Submit(LoadPortCommand command);

    /// <summary>
    /// 作废一条在途指令：让出它的在途位（同名指令能再发），指令以失败落终态。回复丢了、等超时了用；已经不在途的只落终态。
    /// </summary>
    void Abandon(LoadPortCommand command, string reason);

    /// <summary>
    /// 作废全部在途指令（动作没做成、断线时用）。
    /// </summary>
    void AbandonAll(string reason);
}
