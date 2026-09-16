namespace xyz.Modules;

/// <summary>
/// E84 握手期间设备侧反查 EAP 的口子：置 L_REQ/U_REQ、给 READY 之前按这里的结果判断能不能接受这次交接。
/// 实现由 EAP 侧提供并挂到 ILoadPort.E84Provider；未接 EAP 时为 null，设备侧按本地开关自行决定。
/// </summary>
public interface IE84Provider
{
    /// <summary>端口当前的 E87 搬运状态：决定这次是能送盒进来还是能把盒取走。</summary>
    LoadPortTransferState GetTransferState(ILoadPort port);

    /// <summary>端口是否处于自动（AMHS 可交接）模式。</summary>
    bool IsAutoAccessMode(ILoadPort port);

    /// <summary>端口是否已被 Host 预约给某个载具；预约给别人时不接受交接。</summary>
    bool IsReserved(ILoadPort port);
}
