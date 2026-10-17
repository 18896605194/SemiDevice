namespace xyz.Components.Interfaces;

/// <summary>
/// E40（PJ）
/// </summary>
public interface IE40Component
{
    /// <summary>
    /// 接到链路、GEM、对象服务和 Job 管理上
    /// </summary>
    void Attach(IHsmsComponent link, IE30Component gem, IE39Component? objects, IJobManager jobs);

    /// <summary>
    /// 从设备上摘下来（宿主退出）
    /// </summary>
    void Detach();
}
