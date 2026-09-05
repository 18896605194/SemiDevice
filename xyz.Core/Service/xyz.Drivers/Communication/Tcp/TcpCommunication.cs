namespace xyz.Drivers.Communication.Tcp;

/// <summary>
/// TCP 传输的具体实现。帧解析交给上层通道轮询处理，ParseReceivedData 无操作。
/// </summary>
public class TcpCommunication : TcpBase
{
    protected override void ParseReceivedData(byte[] data)
    {
    }
}
