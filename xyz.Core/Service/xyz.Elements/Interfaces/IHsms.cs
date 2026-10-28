using xyz.Secs.Gem;
using xyz.Elements.Attributes;

namespace xyz.Elements.Interfaces;

/// <summary>
/// HSMS 链路
/// </summary>
[Ioc]
public interface IHsms
{
    /// <summary>
    /// 链路本身（xyz.Secs）：各标准往上登记处理方、闸门，设备主动发的报文也经它
    /// </summary>
    HsmsLink Link { get; }

    /// <summary>
    /// 链路选上了（Host 连上、Select 成功）
    /// </summary>
    bool IsSelected { get; }

    /// <summary>
    /// 链路启用了没有（machine.xml 的 IsEnable）
    /// </summary>
    bool IsEnable { get; }

    /// <summary>
    /// 打开链路（开始听 Host 连进来）
    /// </summary>
    void Open();

    /// <summary>
    /// 断开链路（发 Separate）
    /// </summary>
    void Close();
}
