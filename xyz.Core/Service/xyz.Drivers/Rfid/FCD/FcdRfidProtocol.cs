using System.Text;

namespace xyz.Drivers.Rfid.FCD;

/// <summary>
/// FCD（富创得）RFID 读头协议：握手控制字符、消息 ID、块封装。
/// 数据帧 = [LEN][MSG_ID + DATA][CHK_HI][CHK_LO]：LEN 是块字节数（含 MSG_ID），CHK 是块内字节和（高位在前）。
/// 收发都是二进制，经 Latin1 无损走现有的字符串帧通讯。
///
/// ⚠ 校验和**不含 LEN 字节**——这一点跟老一代 Kxware 驱动相反（那版把 LEN 也加进和里）。
///   本实现照 XM.Core 在用的那版写；真机首次联调时先拿 GetVersion 验一下校验，不通就是这里。
/// </summary>
public static class FcdRfidProtocol
{
    /// <summary>ISO-8859-1：0x00-0xFF 与 U+0000-U+00FF 一一对应，二进制过 string 管道无损可逆。</summary>
    public static readonly Encoding Binary = Encoding.Latin1;

    #region 握手控制字符

    /// <summary>请求发送（发起方问对面能不能收）。</summary>
    public const byte Enq = 0x05;

    /// <summary>准备接收（应答 ENQ）。</summary>
    public const byte Eot = 0x04;

    /// <summary>收妥。</summary>
    public const byte Ack = 0x06;

    /// <summary>拒收（校验错或不受理）。</summary>
    public const byte Nak = 0x15;

    #endregion

    #region 消息 ID

    /// <summary>取读头版本。</summary>
    public const byte CmdGetVersion = 0x00;

    /// <summary>取读头状态。</summary>
    public const byte CmdGetStatus = 0x01;

    /// <summary>读标签。</summary>
    public const byte CmdReadTag = 0xA0;

    /// <summary>版本响应。</summary>
    public const byte RspVersion = 0x60;

    /// <summary>状态响应。</summary>
    public const byte RspStatus = 0x61;

    /// <summary>动作完成（首字节是完成的指令号）；不终结在途指令，当主动消息上抛。</summary>
    public const byte RspOperationCompleted = 0x62;

    /// <summary>错误响应：可终结任何在途指令。</summary>
    public const byte RspError = 0x63;

    /// <summary>读头主动事件：不认领在途指令。</summary>
    public const byte RspEvent = 0x66;

    /// <summary>标签数据响应。</summary>
    public const byte RspTagData = 0xE0;

    #endregion

    /// <summary>是不是握手控制字符（控制帧恒为单字节）。</summary>
    public static bool IsControl(byte value)
    {
        return value is Enq or Eot or Ack or Nak;
    }

    /// <summary>
    /// 块封壳：[LEN][块][CHK_HI][CHK_LO]。
    /// </summary>
    public static byte[] WrapBlock(byte[] block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var frame = new byte[1 + block.Length + 2];
        frame[0] = (byte)block.Length;
        Array.Copy(block, 0, frame, 1, block.Length);

        int checksum = Checksum(block, 0, block.Length);
        frame[^2] = (byte)((checksum >> 8) & 0xFF);
        frame[^1] = (byte)(checksum & 0xFF);
        return frame;
    }

    /// <summary>
    /// 拆块并验校验和；成功输出 MSG_ID 与其后的数据。
    /// 校验不过返回 false，调用方应回 NAK。
    /// </summary>
    public static bool TryUnwrapBlock(byte[] frame, out byte messageId, out byte[] data)
    {
        messageId = 0;
        data = Array.Empty<byte>();

        // 最小块：LEN + MSG_ID + CHK*2
        if (frame is null || frame.Length < 4)
        {
            return false;
        }

        int length = frame[0];
        if (length < 1 || frame.Length < 1 + length + 2)
        {
            return false;
        }

        int received = (frame[1 + length] << 8) | frame[1 + length + 1];
        if (received != Checksum(frame, 1, length))
        {
            return false;
        }

        messageId = frame[1];
        data = new byte[length - 1];
        Array.Copy(frame, 2, data, 0, length - 1);
        return true;
    }

    /// <summary>
    /// 一帧总长：LEN + 块 + 两字节校验。
    /// </summary>
    public static int FrameLength(byte lengthByte)
    {
        return 1 + lengthByte + 2;
    }

    private static int Checksum(byte[] buffer, int offset, int count)
    {
        int sum = 0;
        for (int index = 0; index < count; index++)
        {
            sum += buffer[offset + index];
        }

        return sum & 0xFFFF;
    }
}
