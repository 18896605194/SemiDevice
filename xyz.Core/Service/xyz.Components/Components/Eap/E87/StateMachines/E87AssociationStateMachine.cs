namespace xyz.Components.Components;

/// <summary>端口跟载具的关联（数值照 SEMI E87 的 PortAssociationState）。</summary>
internal enum E87AssociationState : byte
{
    NotAssociated = 0,
    Associated = 1,
}

/// <summary>关联状态机收的消息。</summary>
internal enum E87AssociationMessage
{
    Associate,
    Dissociate,
}

/// <summary>
/// 端口关联状态机（E87 Load Port / Carrier Association）：读到号或 Host 给了号就关联；Host 取消、载具拿走就取消关联（跟 CTC 一样）。
/// </summary>
internal sealed class E87AssociationStateMachine : E87StateMachine<E87AssociationState, E87AssociationMessage>
{
    private readonly E87Port _port;

    public E87AssociationStateMachine(E87Port port) : base(E87AssociationState.NotAssociated)
    {
        _port = port;
        Add(E87AssociationState.NotAssociated, E87AssociationMessage.Associate, E87AssociationState.Associated, EnterAssociated);
        Add(E87AssociationState.Associated, E87AssociationMessage.Dissociate, E87AssociationState.NotAssociated, EnterNotAssociated);
    }

    private void EnterAssociated(E87AssociationState from)
    {
        _port.Owner.ReportPort(_port.Owner.AssociationGo, _port);
    }

    private void EnterNotAssociated(E87AssociationState from)
    {
        _port.Owner.ReportPort(_port.Owner.AssociationGoNot, _port);
    }
}
