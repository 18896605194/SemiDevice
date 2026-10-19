using xyz.Components.Enums;

namespace xyz.Components.Interfaces;

/// <summary>
/// E84 握手期间设备侧反查 EAP 的口子
/// </summary>
public interface IE84Provider
{
    /// <summary>
    /// 端口当前的 E87 搬运状态
    /// </summary>
    /// <param name="port"></param>
    /// <returns></returns>
    LoadPortTransferState GetTransferState(ILoadPortAccess port);

    /// <summary>
    /// 端口是否处于自动（AMHS 可交接）模式
    /// </summary>
    /// <param name="port"></param>
    /// <returns></returns>
    bool IsAutoAccessMode(ILoadPortAccess port);
}
