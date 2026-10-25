using xyz.Drivers.Communication;
using xyz.Drivers.Device;

namespace xyz.Components.Interfaces;

/// <summary>
/// 设备通道：模块经它把指令发下去、收回复；不认识任何品牌，也没有固定动作
/// </summary>
public interface IDeviceDriverComponent
{
    /// <summary>
    /// 启用了没有：设备只用启用的那条通道（串口、网口都配上时选一条）
    /// </summary>
    bool IsEnable { get; }

    /// <summary>
    /// 连上了没有
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 设备主动上报的报文（没有在途指令认的），在解析线程上回调
    /// </summary>
    event Action<object>? Reported;

    /// <summary>
    /// 用哪家的解码器：所属模块开机时交给它，要在通道打开之前
    /// </summary>
    void UseCodec(IFrameCodec codec);

    /// <summary>
    /// 发一条指令；没连上、同键指令还在途就被拒（false）
    /// </summary>
    bool Send(DeviceCommand command);

    /// <summary>
    /// 作废一条在途指令（回复丢了、等超时了），同键指令能再发
    /// </summary>
    void Abandon(DeviceCommand command, string reason);

    /// <summary>
    /// 作废全部在途指令
    /// </summary>
    void AbandonAll(string reason);

    /// <summary>
    /// 关连接，不再重连
    /// </summary>
    void Close();
}
