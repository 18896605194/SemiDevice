using xyz.Components.Interfaces;
using xyz.Shared.Dtos;

namespace xyz.Components.Components;

/// <summary>载具 ID 状态（数值照 SEMI E87 的 CarrierIDStatus）；NoCarrier 是端口上没有 E87 载具对象。</summary>
internal enum E87CarrierIdState : byte
{
    WaitingForHost = 1,
    Verified = 2,
    VerifyFailed = 3,
    NoCarrier = 255,
}

/// <summary>载具 ID 状态机收的消息。</summary>
internal enum E87CarrierIdMessage
{
    /// <summary>设备读到了号。</summary>
    IdRead,

    /// <summary>Host ProceedWithCarrier 认定（读码失败的端口，Host 带端口号给号也是它）。</summary>
    HostProceed,

    /// <summary>Host 取消这个载具。</summary>
    Cancel,

    /// <summary>删载具对象：载具拿走、Host CarrierReCreate。</summary>
    Delete,
}

/// <summary>
/// 载具 ID 状态机（E87 Carrier ID Status），载具对象的有无也在这里（NoCarrier = 没有）：
/// 读到号 → 建对象、等 Host（#1、#3）；Host 给号 → 建对象、认定（#1、#4）；等 Host 时认定（#8）或取消（#9）；删对象（#21）。
/// 一认定就写回设备、Load 起来读槽图（CTC 就是在进"认定"时调 Load）。
/// </summary>
internal sealed class E87CarrierIdStateMachine : E87StateMachine<E87CarrierIdState, E87CarrierIdMessage>
{
    /// <summary>Reason 5：Host 取消了这个载具（跟 #9 一起报）。</summary>
    private const byte ReasonHostCancel = 5;

    private readonly E87Port _port;
    private readonly ICarrier _carrier;

    public E87CarrierIdStateMachine(E87Port port) : base(E87CarrierIdState.NoCarrier)
    {
        _port = port;
        _carrier = port.Carrier;
        Add(E87CarrierIdState.NoCarrier, E87CarrierIdMessage.IdRead, E87CarrierIdState.WaitingForHost, EnterWaitingForHost);
        Add(E87CarrierIdState.NoCarrier, E87CarrierIdMessage.HostProceed, E87CarrierIdState.Verified, EnterVerified);
        Add(E87CarrierIdState.WaitingForHost, E87CarrierIdMessage.HostProceed, E87CarrierIdState.Verified, EnterVerified);
        Add(E87CarrierIdState.WaitingForHost, E87CarrierIdMessage.Cancel, E87CarrierIdState.VerifyFailed, EnterVerifyFailed);
        AddFromAnyState(E87CarrierIdMessage.Delete, E87CarrierIdState.NoCarrier, EnterNoCarrier);
    }

    private void EnterWaitingForHost(E87CarrierIdState from)
    {
        var owner = _port.Owner;
        owner.ReportCarrier(owner.CarrierTrans01, _port);
        owner.ReportCarrier(owner.CarrierTrans03, _port);
        _carrier.UpdateStatus(CarrierIdStatus.WaitingForHost, null);
    }

    private void EnterVerified(E87CarrierIdState from)
    {
        var owner = _port.Owner;
        if (from == E87CarrierIdState.NoCarrier)
        {
            owner.ReportCarrier(owner.CarrierTrans01, _port);
            owner.ReportCarrier(owner.CarrierTrans04, _port);
        }
        else
        {
            owner.ReportCarrier(owner.CarrierTrans08, _port);
        }

        _carrier.SetId(_port.CarrierId);
        _port.LoadLater();
    }

    private void EnterVerifyFailed(E87CarrierIdState from)
    {
        _port.Owner.ReportCarrier(_port.Owner.CarrierTrans09, _port, ReasonHostCancel);
        _carrier.UpdateStatus(CarrierIdStatus.VerifyFailed, null);
    }

    private void EnterNoCarrier(E87CarrierIdState from)
    {
        if (from != E87CarrierIdState.NoCarrier)
        {
            _port.Owner.ReportCarrier(_port.Owner.CarrierTrans21, _port);
        }
    }
}
