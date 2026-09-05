namespace xyz.Drivers.Communication.Serial;

/// <summary>
/// 串口传输的具体实现。帧解析交给上层通道轮询处理，ParseReceivedData 无操作。
/// </summary>
public class SerialCommunication : SerialPortBase
{
    protected override void ParseReceivedData(byte[] data)
    {
    }
}
