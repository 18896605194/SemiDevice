using xyz.Elements.Attributes;

namespace xyz.Elements.Interfaces;

/// <summary>
/// E94（CJ）
/// </summary>
[Ioc]
public interface IE94Element
{
    /// <summary>
    /// 接到链路、GEM、对象服务和 Job 管理上
    /// </summary>
    void Attach(IHsms link, IE30Element gem, IE39Element? objects, IJobManager jobs);

    /// <summary>
    /// 从设备上摘下来（宿主退出）
    /// </summary>
    void Detach();
}
