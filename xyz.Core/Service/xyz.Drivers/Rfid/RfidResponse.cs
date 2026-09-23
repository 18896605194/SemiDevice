namespace xyz.Drivers.Rfid;

/// <summary>
/// RFID 指令结果（厂商无关）：上层只读本对象，不碰品牌指令的内部字节。
/// </summary>
public sealed class RfidResponse
{
    /// <summary>指令是否成功；设备回错误块、拒收或超时均为 false。</summary>
    public bool IsSuccess { get; private init; }

    /// <summary>读到的载具 ID；只有读码指令成功时有值。</summary>
    public string? CarrierId { get; private init; }

    /// <summary>设备原始数据（已去掉 MSG_ID），诊断用。</summary>
    public byte[] Data { get; private init; } = Array.Empty<byte>();

    /// <summary>失败原因，成功时为空。</summary>
    public string Error { get; private init; } = string.Empty;

    public static RfidResponse Ok(byte[] data, string? carrierId = null)
    {
        return new RfidResponse { IsSuccess = true, Data = data, CarrierId = carrierId };
    }

    public static RfidResponse Fail(string error)
    {
        return new RfidResponse { IsSuccess = false, Error = error };
    }
}
