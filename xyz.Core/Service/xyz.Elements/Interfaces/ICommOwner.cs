using xyz.Drivers.Communication;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 下面挂了设备通道的元件（模块、读头）：开机时把自己交给通道，通道照它要解码器
/// </summary>
public interface ICommOwner
{
    /// <summary>
    /// 本品牌的解码器，通道开机前要一次
    /// </summary>
    IFrameCodec CreateCodec();
}
