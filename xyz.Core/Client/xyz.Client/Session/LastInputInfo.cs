using System.Runtime.InteropServices;

namespace xyz.Client.Session;

/// <summary>
/// GetLastInputInfo 的参数：最后一次键盘、鼠标、触摸的时刻
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LastInputInfo
{
    /// <summary>
    /// 这个结构的字节数，调用前填好
    /// </summary>
    public uint Size;

    /// <summary>
    /// 最后一次输入的时刻（开机以来的毫秒数，低 32 位）
    /// </summary>
    public uint Time;
}
