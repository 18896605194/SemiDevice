namespace xyz.Tools;

/// <summary>
/// 订阅凭据：Dispose 一次就退订，再 Dispose 不再动
/// </summary>
public sealed class Subscription(Action unsubscribe) : IDisposable
{
    private Action? _unsubscribe = unsubscribe;

    /// <summary>
    /// 退订
    /// </summary>
    public void Dispose()
    {
        // 两个线程同时 Dispose 时只退一次
        Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
