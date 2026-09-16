namespace xyz.Modules;

/// <summary>
/// E84 自动交接的设备侧上报口：搬运车（OHT/AGV）与本端口的一次交接，进展由模块调这里告诉 EAP。
/// 实现由 EAP 侧提供并挂到 ILoadPort.E84Callback；未接 EAP 或本机没有 E84 硬件时为 null，模块照常运行。
/// 与 E87 回调共用同一条派发线程，按发生顺序串行调用。
/// </summary>
public interface IE84Callback
{
    /// <summary>交接开始（已握手到搬运车开始进出料）；isLoad = true 为送盒进来，false 为把盒取走。</summary>
    void HandoffStarted(ILoadPort port, bool isLoad);

    /// <summary>交接正常结束，载具已完成交接。</summary>
    void HandoffCompleted(ILoadPort port, bool isLoad);

    /// <summary>某一段握手超时，本次交接中止；EAP 据此报警。</summary>
    void HandoffTimeout(ILoadPort port, bool isLoad, E84Timer timer);

    /// <summary>交接异常中止（信号异常、急停、被取消等）。</summary>
    void HandoffAborted(ILoadPort port, bool isLoad, string reason);

    /// <summary>本端口对搬运车的可交接状态变了（HO_AVBL）。</summary>
    void AvailabilityChanged(ILoadPort port, bool available);
}
