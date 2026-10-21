namespace xyz.Components.Interfaces;

/// <summary>
/// EAP Trace 数据采集（S2F23 / S6F1）
/// </summary>
public interface IE30TraceComponent
{
    /// <summary>
    /// 接到链路和 GEM 上
    /// </summary>
    void Attach(IHsms link, IE30Component gem);

    /// <summary>
    /// 从 GEM 上摘下来，停掉全部 Trace（宿主退出）
    /// </summary>
    void Detach();
}
