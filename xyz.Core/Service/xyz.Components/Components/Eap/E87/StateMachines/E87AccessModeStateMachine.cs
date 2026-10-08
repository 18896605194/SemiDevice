namespace xyz.Components.Components;

/// <summary>端口存取方式（数值照 SEMI E87 的 PortAccessMode）。</summary>
internal enum E87AccessMode : byte
{
    Manual = 0,
    Auto = 1,
}

/// <summary>存取方式状态机收的消息。</summary>
internal enum E87AccessModeMessage
{
    GoManual,
    GoAuto,
}

/// <summary>
/// 端口存取方式状态机（E87 Access Mode）：手动 / 自动，转了报事件。开机照设备当前的方式定，不报；
/// 设备切了（本地切、Host S3F25 / S3F27 切）经回调发消息过来，已经是这个方式的不重复报。
/// </summary>
internal sealed class E87AccessModeStateMachine : E87StateMachine<E87AccessMode, E87AccessModeMessage>
{
    private readonly E87Port _port;

    public E87AccessModeStateMachine(E87Port port) : base(port.Device.IsAutoMode ? E87AccessMode.Auto : E87AccessMode.Manual)
    {
        _port = port;
        Add(E87AccessMode.Manual, E87AccessModeMessage.GoAuto, E87AccessMode.Auto, EnterAuto);
        Add(E87AccessMode.Auto, E87AccessModeMessage.GoManual, E87AccessMode.Manual, EnterManual);
    }

    private void EnterAuto(E87AccessMode from)
    {
        _port.Owner.ReportPort(_port.Owner.AccessGoAuto, _port);
    }

    private void EnterManual(E87AccessMode from)
    {
        _port.Owner.ReportPort(_port.Owner.AccessGoManual, _port);
    }
}
