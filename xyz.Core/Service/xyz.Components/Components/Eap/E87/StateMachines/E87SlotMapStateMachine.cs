using xyz.Shared.Dtos;

namespace xyz.Components.Components;

/// <summary>槽图状态（数值照 SEMI E87 的 SlotMapStatus）；NoCarrier 是端口上没有 E87 载具对象。</summary>
internal enum E87SlotMapState : byte
{
    NotRead = 0,
    WaitingForHost = 1,
    Verified = 2,
    VerifyFailed = 3,
    NoCarrier = 255,
}

/// <summary>槽图状态机收的消息。</summary>
internal enum E87SlotMapMessage
{
    /// <summary>建了载具对象。</summary>
    Create,

    /// <summary>设备读到了槽图。</summary>
    Read,

    /// <summary>Host ProceedWithCarrier 认定槽图。</summary>
    HostProceed,

    /// <summary>Host 取消这个载具。</summary>
    Cancel,

    /// <summary>删载具对象。</summary>
    Delete,
}

/// <summary>
/// 槽图状态机（E87 Slot Map Status）：建对象进没读（#12）；读到槽图一律等 Host 核对（#14，跟 CTC 一样设备不自己认定）；
/// Host 认定（#15，料到了，通知 E90 建片对象）或取消（#16）。
/// </summary>
internal sealed class E87SlotMapStateMachine : E87StateMachine<E87SlotMapState, E87SlotMapMessage>
{
    /// <summary>Reason 0：槽图读到了，等 Host 核对（跟 #14 一起报）。</summary>
    private const byte ReasonVerificationNeeded = 0;

    /// <summary>Reason 5：Host 取消了这个载具（跟 #16 一起报）。</summary>
    private const byte ReasonHostCancel = 5;

    private readonly E87Port _port;

    public E87SlotMapStateMachine(E87Port port) : base(E87SlotMapState.NoCarrier)
    {
        _port = port;
        Add(E87SlotMapState.NoCarrier, E87SlotMapMessage.Create, E87SlotMapState.NotRead, EnterNotRead);
        Add(E87SlotMapState.NotRead, E87SlotMapMessage.Read, E87SlotMapState.WaitingForHost, EnterWaitingForHost);
        Add(E87SlotMapState.WaitingForHost, E87SlotMapMessage.HostProceed, E87SlotMapState.Verified, EnterVerified);
        Add(E87SlotMapState.WaitingForHost, E87SlotMapMessage.Cancel, E87SlotMapState.VerifyFailed, EnterVerifyFailed);
        AddFromAnyState(E87SlotMapMessage.Delete, E87SlotMapState.NoCarrier);
    }

    private void EnterNotRead(E87SlotMapState from)
    {
        _port.Owner.ReportCarrier(_port.Owner.CarrierTrans12, _port);
    }

    private void EnterWaitingForHost(E87SlotMapState from)
    {
        _port.Owner.ReportCarrier(_port.Owner.CarrierTrans14, _port, ReasonVerificationNeeded);
        _port.Device.UpdateCarrierStatus(null, CarrierSlotMapStatus.WaitingForHost);
    }

    private void EnterVerified(E87SlotMapState from)
    {
        _port.Owner.ReportCarrier(_port.Owner.CarrierTrans15, _port);
        _port.Device.UpdateCarrierStatus(null, CarrierSlotMapStatus.Verified);
        _port.Owner.NotifyMaterialVerifiedLater(_port);
    }

    private void EnterVerifyFailed(E87SlotMapState from)
    {
        _port.Owner.ReportCarrier(_port.Owner.CarrierTrans16, _port, ReasonHostCancel);
        _port.Device.UpdateCarrierStatus(null, CarrierSlotMapStatus.VerifyFailed);
    }
}
