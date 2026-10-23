using xyz.Drivers.Device;

namespace xyz._35021.Module.Drivers.Fcd.Commands;

/// <summary>
/// 富创得 LoadPort 的一条指令：在途键是指令名；回复怎么认（ACK / INF / NAK / ABS）每条指令在自己的 Handle 里写
/// </summary>
public abstract class FcdCommand : DeviceCommand<FcdMessage>
{
    /// <summary>
    /// 在途键 = 指令名
    /// </summary>
    public override string Key => Name;

    /// <summary>
    /// 指令名（CLOAD、STATE 这类），回复按它对号
    /// </summary>
    protected abstract string Name { get; }
}
