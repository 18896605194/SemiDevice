namespace xyz.Secs.SecsII;

/// <summary>
/// SECS-II 数据格式码（SEMI E5 表 1）。
/// 字节高 6 位是格式本体，低 2 位在线上借给"长度字节数"用，这里只留格式值（低 2 位恒 0）。
/// </summary>
public enum SecsFormat : byte
{
    /// <summary>列表（长度 = 子项个数，无数据体）。</summary>
    List = 0x00,

    /// <summary>二进制字节流。</summary>
    Binary = 0x20,

    /// <summary>布尔数组，每元素 1 字节 0/1。</summary>
    Boolean = 0x24,

    /// <summary>ASCII 字符串。</summary>
    Ascii = 0x28,

    /// <summary>JIS-8 编码字符串（少见，按字节透明传输）。</summary>
    Jis8 = 0x2C,

    I1 = 0x40,
    I2 = 0x48,
    I4 = 0x50,
    I8 = 0x58,
    F4 = 0x60,
    F8 = 0x68,
    U1 = 0x70,
    U2 = 0x78,
    U4 = 0x80,
    U8 = 0x88,
}
