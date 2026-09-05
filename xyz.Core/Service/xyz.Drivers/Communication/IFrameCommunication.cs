namespace xyz.Drivers.Communication;

/// <summary>
/// 帧级通讯接口：以帧体为单位收发的通讯抽象。
/// 实现负责组合字节传输（ICommunication）与帧编解码（IFrameCodec），
/// 驱动只依赖本接口，不关心传输方式与帧格式。
/// </summary>
public interface IFrameCommunication
{
    /// <summary>
    /// 收到一条完整帧体（已去壳），在实现内部接收线程触发；订阅方应及时返回。
    /// </summary>
    event Action<string>? FrameReceived;

    /// <summary>
    /// 通讯连接是否可用。
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 打开连接并启动接收。
    /// </summary>
    bool Open();

    /// <summary>
    /// 停止接收并关闭连接。
    /// </summary>
    void Close();

    /// <summary>
    /// 发送一条帧体（由实现统一包壳）。
    /// </summary>
    void Send(string body);
}
