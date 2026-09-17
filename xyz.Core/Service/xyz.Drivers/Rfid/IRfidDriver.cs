namespace xyz.Drivers.Rfid;

/// <summary>
/// RFID 读头驱动契约：读头组件只认这个接口，不认具体驱动类。
/// </summary>
public interface IRfidDriver
{
    /// <summary>
    /// 读头主动上报，在驱动路由线程回调，订阅方应及时返回。
    /// </summary>
    event Action<RfidResponse>? OnSpontaneousEvent;

    bool IsConnected { get; }

    bool Open();

    void Close();

    /// <summary>
    /// 受理一条指令：占住唯一的在途位并下发；未连接或上一条还没终结时返回 false。
    /// </summary>
    bool Submit(RfidCommand command);

    /// <summary>
    /// 把在途指令以失败终结并让出在途位（超时时用，否则之后再也提交不上）。
    /// </summary>
    void AbandonInflight(string reason);
}
