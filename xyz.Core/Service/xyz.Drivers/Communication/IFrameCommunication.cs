namespace xyz.Drivers.Communication;

/// <summary>
/// 帧级通讯：字节传输（ICommunication）加解码器（IFrameCodec），收上来的已经是解好的报文，发的时候给帧体
/// </summary>
public interface IFrameCommunication
{
    /// <summary>
    /// 收到一条解好的报文，在接收线程上触发，订阅方要及时返回
    /// </summary>
    event Action<object>? MessageReceived;

    /// <summary>
    /// 连接是否可用
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 打开连接并开始接收
    /// </summary>
    bool Open();

    /// <summary>
    /// 停止接收并关闭连接
    /// </summary>
    void Close();

    /// <summary>
    /// 发一条帧体（壳由解码器包）
    /// </summary>
    void Send(string body);
}
