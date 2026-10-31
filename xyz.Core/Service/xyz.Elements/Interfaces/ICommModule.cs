using xyz.Drivers.Device;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 要通道替它轮询状态、收主动上报的模块；挂通道还要实现 ICommOwner
/// </summary>
public interface ICommModule
{
    /// <summary>
    /// 一圈查什么、回来写到哪，从上往下写；不查状态的 yield break
    /// </summary>
    IEnumerable<DeviceCommand> QueryDataLoop();

    /// <summary>
    /// 断线、查询超时：把查到的状态清掉，界面显示不知道
    /// </summary>
    void ClearQueriedStatus();

    /// <summary>
    /// 连接刚断那一拍（扫描线程）：跟着连接走的东西在这里清掉
    /// </summary>
    void OnDisconnected();

    /// <summary>
    /// 设备主动上报（没有在途指令认的报文），在通道的解析线程上调，不是扫描线程
    /// </summary>
    void OnReport(object message);
}
