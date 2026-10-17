namespace xyz.Components.Interfaces;

/// <summary>
/// E90（片跟踪）
/// </summary>
public interface IE90Component
{
    /// <summary>
    /// 接到 GEM、对象服务和晶圆账上
    /// </summary>
    void Attach(IE30Component gem, IE39Component? objects, IWaferManagerComponent ledger, bool waitForCarrier);

    /// <summary>
    /// 从设备上摘下来（宿主退出）
    /// </summary>
    void Detach();

    /// <summary>
    /// 这个口的料认定了，建片对象
    /// </summary>
    void MaterialArrived(string loadPort);
}
