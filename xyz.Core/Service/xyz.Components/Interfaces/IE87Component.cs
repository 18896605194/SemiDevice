namespace xyz.Components.Interfaces;

/// <summary>
/// E87（载具管理）
/// </summary>
public interface IE87Component
{

    void Attach(IHsms link, IE30Component gem, IReadOnlyList<ILoadPortAccess> ports, Action<string>? materialVerified);

    void Detach();
}
