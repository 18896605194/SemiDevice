using xyz.Client.Common.Events;
using xyz.Common.Events;

/// <summary>
/// 冒烟里没有真正的客户端：后端经 RemoteEventPublisher 发出的消息直接交给本进程的 RemoteEventSubscriber，当作客户端收到。各冒烟工程链接这一个文件
/// </summary>
static class SmokeEvents
{
    private static IDisposable? _loopback;

    /// <summary>
    /// 冒烟开头调一次：之后 RemoteEventSubscriber.Register 能收到本进程发的消息（经过一次打包、拆包，在发的那个线程上回调）
    /// </summary>
    public static void Loopback()
    {
        if (_loopback is not null)
        {
            return;
        }

        _loopback = RemoteEventPublisher.SubscribeSent(RemoteEventSubscriber.Deliver);
    }
}
