namespace xyz.Components.Components;

/// <summary>载具取放状态（数值照 SEMI E87 的 CarrierAccessingStatus）；NoCarrier 是端口上没有 E87 载具对象。</summary>
internal enum E87AccessState : byte
{
    NotAccessed = 0,
    InAccess = 1,
    CarrierComplete = 2,
    CarrierStopped = 3,
    NoCarrier = 255,
}

/// <summary>取放状态机收的消息。</summary>
internal enum E87AccessMessage
{
    /// <summary>建了载具对象。</summary>
    Create,

    /// <summary>开始取放（LoadPort Load 好了，照 CTC）。</summary>
    Start,

    /// <summary>干完了（CJ 完成）。</summary>
    Complete,

    /// <summary>中断（取放过、没干完就 Unload 了，或端口出错）。</summary>
    Stop,

    /// <summary>删载具对象。</summary>
    Delete,
}

/// <summary>
/// 载具取放状态机（E87 Carrier Accessing Status）：建对象进没取放（#17）、开始取放（#18）、干完（#19）、中断（#20）。
/// 干完了自动 Unload 不归这里：LoadPort 自己按它的 EC AutoUnload 卸（接不接 EAP 都一样）；关着的等 Host CarrierRelease 或操作员 Unload。
/// </summary>
internal sealed class E87AccessStateMachine : E87StateMachine<E87AccessState, E87AccessMessage>
{
    private readonly E87Port _port;

    public E87AccessStateMachine(E87Port port) : base(E87AccessState.NoCarrier)
    {
        _port = port;
        Add(E87AccessState.NoCarrier, E87AccessMessage.Create, E87AccessState.NotAccessed, EnterNotAccessed);
        Add(E87AccessState.NotAccessed, E87AccessMessage.Start, E87AccessState.InAccess, EnterInAccess);
        Add(E87AccessState.InAccess, E87AccessMessage.Complete, E87AccessState.CarrierComplete, EnterComplete);
        Add(E87AccessState.InAccess, E87AccessMessage.Stop, E87AccessState.CarrierStopped, EnterStopped);
        AddFromAnyState(E87AccessMessage.Delete, E87AccessState.NoCarrier);
    }

    private void EnterNotAccessed(E87AccessState from)
    {
        _port.Owner.ReportCarrier(_port.Owner.CarrierTrans17, _port);
    }

    private void EnterInAccess(E87AccessState from)
    {
        _port.Owner.ReportCarrier(_port.Owner.CarrierTrans18, _port);
    }

    private void EnterComplete(E87AccessState from)
    {
        _port.Owner.ReportCarrier(_port.Owner.CarrierTrans19, _port);
    }

    private void EnterStopped(E87AccessState from)
    {
        _port.Owner.ReportCarrier(_port.Owner.CarrierTrans20, _port);
    }
}
