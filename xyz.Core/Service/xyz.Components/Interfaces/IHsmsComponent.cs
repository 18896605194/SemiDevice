using xyz.Components.Models;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;

namespace xyz.Components.Interfaces;

/// <summary>
/// HSMS 链路
/// </summary>
public interface IHsmsComponent
{
    /// <summary>
    /// 链路选上了（Host 连上、Select 成功）
    /// </summary>
    bool IsSelected { get; }

    /// <summary>
    /// 收到的消息先过这一道
    /// </summary>
    Func<HsmsMessage, SecsReply?>? Gate { get; set; }

    /// <summary>
    /// 链路选上了
    /// </summary>
    event Action? LinkSelected;

    /// <summary>
    /// 链路断了（带原因）
    /// </summary>
    event Action<string>? LinkClosed;

    /// <summary>
    /// 登记一种消息（S、F）的处理
    /// </summary>
    void Handle(byte stream, byte function, Func<HsmsMessage, Task<SecsReply>> handler);

    /// <summary>
    /// 登记一种消息（S、F）的处理
    /// </summary>
    void Handle(byte stream, byte function, Func<HsmsMessage, SecsReply> handler);

    /// <summary>
    /// 发一条消息，等回复
    /// </summary>
    Task<HsmsMessage> SendAsync(SecsMessage message);

    /// <summary>
    /// 链路启用了没有（sc 的 IsEnable）
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
