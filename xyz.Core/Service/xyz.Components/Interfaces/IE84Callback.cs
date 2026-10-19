using xyz.Components.Enums;

namespace xyz.Components.Interfaces;

/// <summary>
/// E84 自动交接的设备侧上报口：搬运车（OHT/AGV）与本端口的一次交接
/// </summary>
public interface IE84Callback
{
    /// <summary>
    /// 交接开始（已握手到搬运车开始进出料）；isLoad = true 为送盒进来，false 为把盒取走
    /// </summary>
    /// <param name="port"></param>
    /// <param name="isLoad"></param>
    void HandoffStarted(ILoadPortAccess port, bool isLoad);

    /// <summary>
    /// 交接正常结束，载具已完成交接
    /// </summary>
    /// <param name="port"></param>
    /// <param name="isLoad"></param>
    void HandoffCompleted(ILoadPortAccess port, bool isLoad);

    /// <summary>
    /// 一段握手超时，本次交接中
    /// </summary>
    /// <param name="port"></param>
    /// <param name="isLoad"></param>
    /// <param name="timer"></param>
    void HandoffTimeout(ILoadPortAccess port, bool isLoad, E84Timer timer);

    /// <summary>
    /// 交接异常中止
    /// </summary>
    /// <param name="port"></param>
    /// <param name="isLoad"></param>
    /// <param name="reason"></param>
    void HandoffAborted(ILoadPortAccess port, bool isLoad, string reason);

    /// <summary>
    /// 本端口对搬运车的可交接状态变了（HO_AVBL）
    /// </summary>
    /// <param name="port"></param>
    /// <param name="available"></param>
    void AvailabilityChanged(ILoadPortAccess port, bool available);
}
