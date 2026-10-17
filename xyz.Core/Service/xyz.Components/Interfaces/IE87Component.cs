namespace xyz.Components.Interfaces;

/// <summary>
/// E87（载具管理）
/// </summary>
public interface IE87Component
{
    /// <summary>
    /// 接到链路、GEM 和各 LoadPort 上；槽图认定后调 materialVerified（E90 建片对象）
    /// </summary>
    void Attach(IHsmsComponent link, IE30Component gem, IReadOnlyList<ILoadPort> ports, Action<string>? materialVerified);

    /// <summary>
    /// 从设备上摘下来（宿主退出）
    /// </summary>
    void Detach();
}
