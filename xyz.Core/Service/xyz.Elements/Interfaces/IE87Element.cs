namespace xyz.Elements.Interfaces;

/// <summary>
/// E87（载具管理）
/// </summary>
public interface IE87Element
{
    /// <summary>
    /// 接到链路、GEM 和各 LoadPort 上；substrates 是槽图认定（料到了）时通知的 E90，没接 E90 为 null
    /// </summary>
    void Attach(IHsms link, IE30Element gem, IReadOnlyList<ILoadPortAccess> ports, IE90Element? substrates);

    /// <summary>
    /// 从设备上摘下来（宿主退出）
    /// </summary>
    void Detach();
}
