using xyz.Components.Enums;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>端口搬运状态（数值照 SEMI E87 的 PortTransferState，上报用）；NoState 是还没定过。</summary>
internal enum E87TransferState : byte
{
    OutOfService = 0,
    TransferBlocked = 1,
    ReadyToLoad = 2,
    ReadyToUnload = 3,
    NoState = 255,
}

/// <summary>搬运状态机收的消息：转到哪个状态。</summary>
internal enum E87TransferMessage
{
    GoOutOfService,
    GoBlocked,
    GoReadyToLoad,
    GoReadyToUnload,
}

/// <summary>
/// 端口搬运状态机（E87 Load Port Transfer）：停用 / 挡着 / 等送 / 等取，每个转换报一个事件（#1~#9）。
/// 该在哪个状态由 <see cref="Compute"/> 按 Host 的启停用、设备的忙闲和"这一盒放行了"算出来；
/// EC PortPollMs 定时、每次设备回调和 Host 动作之后都调 <see cref="Refresh"/> 转过去（CTC 只在两个状态里定时查，这里哪个状态都查，不会卡住）。
/// </summary>
internal sealed class E87TransferStateMachine : E87StateMachine<E87TransferState, E87TransferMessage>
{
    private readonly E87Port _port;
    private readonly ICarrier _carrier;

    public E87TransferStateMachine(E87Port port) : base(E87TransferState.NoState)
    {
        _port = port;
        _carrier = port.Carrier;

        // 第一次定状态（#1）
        Add(E87TransferState.NoState, E87TransferMessage.GoOutOfService, E87TransferState.OutOfService, EnterFirst);
        Add(E87TransferState.NoState, E87TransferMessage.GoBlocked, E87TransferState.TransferBlocked, EnterFirst);
        Add(E87TransferState.NoState, E87TransferMessage.GoReadyToLoad, E87TransferState.ReadyToLoad, EnterFirst);
        Add(E87TransferState.NoState, E87TransferMessage.GoReadyToUnload, E87TransferState.ReadyToUnload, EnterFirst);

        // Host 停用（#3）
        Add(E87TransferState.TransferBlocked, E87TransferMessage.GoOutOfService, E87TransferState.OutOfService, EnterOutOfService);
        Add(E87TransferState.ReadyToLoad, E87TransferMessage.GoOutOfService, E87TransferState.OutOfService, EnterOutOfService);
        Add(E87TransferState.ReadyToUnload, E87TransferMessage.GoOutOfService, E87TransferState.OutOfService, EnterOutOfService);

        // Host 启用（#2、#4，进了等送 / 等取的再报 #5）
        Add(E87TransferState.OutOfService, E87TransferMessage.GoBlocked, E87TransferState.TransferBlocked, EnterInService);
        Add(E87TransferState.OutOfService, E87TransferMessage.GoReadyToLoad, E87TransferState.ReadyToLoad, EnterInService);
        Add(E87TransferState.OutOfService, E87TransferMessage.GoReadyToUnload, E87TransferState.ReadyToUnload, EnterInService);

        // 等送 / 等取 → 挡着（#6 / #7）
        Add(E87TransferState.ReadyToLoad, E87TransferMessage.GoBlocked, E87TransferState.TransferBlocked, EnterBlocked);
        Add(E87TransferState.ReadyToUnload, E87TransferMessage.GoBlocked, E87TransferState.TransferBlocked, EnterBlocked);

        // 挡着 → 等送 / 等取（#8 / #9）
        Add(E87TransferState.TransferBlocked, E87TransferMessage.GoReadyToLoad, E87TransferState.ReadyToLoad, EnterReady);
        Add(E87TransferState.TransferBlocked, E87TransferMessage.GoReadyToUnload, E87TransferState.ReadyToUnload, EnterReady);
    }

    /// <summary>Host 没停用这个端口（S3F25；开机是启用）。</summary>
    public bool InService { get; set; } = true;

    /// <summary>
    /// 端口现在该是什么搬运状态（E84 也按这个交接）：Host 停用 → 停用；设备说等送 / 等取就是；
    /// Host 取消、放行的这一盒，卸好、端口空闲了 → 等取；别的都是挡着（设备没初始化、出错这类在 E87 里也算挡着，跟 CTC 一样）。
    /// </summary>
    public E87TransferState Compute()
    {
        if (!InService)
        {
            return E87TransferState.OutOfService;
        }

        var device = _port.Device;
        var local = device.LocalTransferState;
        if (local == LoadPortTransferState.ReadyToLoad)
        {
            return E87TransferState.ReadyToLoad;
        }

        if (local == LoadPortTransferState.ReadyToUnload)
        {
            return E87TransferState.ReadyToUnload;
        }

        if (_port.Released && device.IsIdle && _carrier.IsArrived)
        {
            return E87TransferState.ReadyToUnload;
        }

        return E87TransferState.TransferBlocked;
    }

    /// <summary>按 <see cref="Compute"/> 转过去；等送、等取直接互换的，中间先转一下挡着（E87 没有这两个之间的转换）。</summary>
    public void Refresh()
    {
        var next = Compute();
        if (next == State)
        {
            return;
        }

        bool swap = (State == E87TransferState.ReadyToLoad && next == E87TransferState.ReadyToUnload)
            || (State == E87TransferState.ReadyToUnload && next == E87TransferState.ReadyToLoad);
        if (swap)
        {
            Post(E87TransferMessage.GoBlocked);
        }

        Post(MessageFor(next));
    }

    private static E87TransferMessage MessageFor(E87TransferState state)
    {
        switch (state)
        {
            case E87TransferState.OutOfService:
                return E87TransferMessage.GoOutOfService;

            case E87TransferState.ReadyToLoad:
                return E87TransferMessage.GoReadyToLoad;

            case E87TransferState.ReadyToUnload:
                return E87TransferMessage.GoReadyToUnload;

            default:
                return E87TransferMessage.GoBlocked;
        }
    }

    private void EnterFirst(E87TransferState from)
    {
        _port.Owner.ReportPort(_port.Owner.PortTrans01, _port);
    }

    private void EnterOutOfService(E87TransferState from)
    {
        _port.Owner.ReportPort(_port.Owner.PortTrans03, _port);
    }

    private void EnterInService(E87TransferState from)
    {
        var owner = _port.Owner;
        owner.ReportPort(owner.PortTrans02, _port);
        owner.ReportPort(owner.PortTrans04, _port);
        if (State != E87TransferState.TransferBlocked)
        {
            owner.ReportPort(owner.PortTrans05, _port);
        }
    }

    private void EnterBlocked(E87TransferState from)
    {
        var owner = _port.Owner;
        owner.ReportPort(from == E87TransferState.ReadyToLoad ? owner.PortTrans06 : owner.PortTrans07, _port);
    }

    private void EnterReady(E87TransferState from)
    {
        var owner = _port.Owner;
        owner.ReportPort(State == E87TransferState.ReadyToLoad ? owner.PortTrans08 : owner.PortTrans09, _port);
    }
}
