using xyz.Components.Enums;

namespace xyz.Components.Interfaces;

/// <summary>
/// E84 握手期间设备侧反查 EAP 的口子：LoadPort 每拍把这里查到的 Access Mode 和搬运状态交给 E84，E84 自己判能不能交接。
/// 实现由 EAP 侧提供并挂到 ILoadPort.E84Provider；未接 EAP 时为 null，LoadPort 按本地状态自行判断。
/// </summary>
public interface IE84Provider
{
    /// <summary>端口当前的 E87 搬运状态：决定这次是能送盒进来还是能把盒取走。</summary>
    LoadPortTransferState GetTransferState(ILoadPort port);

    /// <summary>端口是否处于自动（AMHS 可交接）模式。</summary>
    bool IsAutoAccessMode(ILoadPort port);
}
