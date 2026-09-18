namespace xyz.Modules;

public enum E84ReportKind
{
    HandoffStarted,
    HandoffCompleted,
    HandoffTimedOut,
    HandoffAborted,
    AvailabilityChanged,
}

/// <summary>
/// E84 组件交给端口的一条交接进展；端口放进 EAP 派发队列，原样转成 IE84Callback 调用。
/// </summary>
public sealed record E84Report(
    E84ReportKind Kind,
    bool IsLoad = false,
    E84Timer? Timer = null,
    string Reason = "",
    bool Available = false)
{
    /// <summary>交接开始（READY 已给出）；isLoad = true 为送盒进来，false 为把盒取走。</summary>
    public static E84Report Started(bool isLoad) => new(E84ReportKind.HandoffStarted, isLoad);

    /// <summary>交接正常结束，或超时后人工 Complete 确认已完成。</summary>
    public static E84Report Completed(bool isLoad) => new(E84ReportKind.HandoffCompleted, isLoad);

    /// <summary>某一段握手超时，本次交接中止。</summary>
    public static E84Report TimedOut(bool isLoad, E84Timer timer) => new(E84ReportKind.HandoffTimedOut, isLoad, timer);

    /// <summary>交接进行中被打断（端口不可交接、光幕被挡、人工 Retry 等）。</summary>
    public static E84Report Aborted(bool isLoad, string reason) => new(E84ReportKind.HandoffAborted, isLoad, Reason: reason);

    /// <summary>HO_AVBL 变了。</summary>
    public static E84Report AvailabilityChanged(bool available) => new(E84ReportKind.AvailabilityChanged, Available: available);

    /// <summary>
    /// 转成 EAP 的 IE84Callback 调用（在端口的 EAP 派发线程上执行）。
    /// </summary>
    public void DispatchTo(IE84Callback callback, ILoadPort port)
    {
        switch (Kind)
        {
            case E84ReportKind.HandoffStarted:
                callback.HandoffStarted(port, IsLoad);
                break;

            case E84ReportKind.HandoffCompleted:
                callback.HandoffCompleted(port, IsLoad);
                break;

            case E84ReportKind.HandoffTimedOut:
                callback.HandoffTimeout(port, IsLoad, Timer!.Value);
                break;

            case E84ReportKind.HandoffAborted:
                callback.HandoffAborted(port, IsLoad, Reason);
                break;

            case E84ReportKind.AvailabilityChanged:
                callback.AvailabilityChanged(port, Available);
                break;
        }
    }
}
