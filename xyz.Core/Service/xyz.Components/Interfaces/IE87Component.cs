namespace xyz.Components.Interfaces;

/// <summary>
/// E87（载具管理）
/// </summary>
public interface IE87Component
{

    void Attach(IHsmsComponent link, IE30Component gem, IReadOnlyList<ILoadPort> ports, Action<string>? materialVerified);

    void Detach();
}
