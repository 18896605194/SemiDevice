using xyz.Drivers.Device;

namespace xyz._35021.Module.Drivers.Reje.Commands;

/// <summary>
/// 锐洁的一条指令：下发体就是在途键；回复怎么认（回显名、错误码 00000000 是成功、内容怎么取）每条指令在自己的 Handle 里写
/// </summary>
public abstract class RejeCommand : DeviceCommand<RejeMessage>
{
    /// <summary>
    /// 在途键 = 下发体：回复只能靠回显名对号，同名指令同一时间只能有一条在途（Pick 带手指、站点、槽号）
    /// </summary>
    public override string Key => Wire;

    /// <summary>
    /// 下发体（不含 '@' 和 ';'），如 G10102
    /// </summary>
    protected abstract string Wire { get; }

    /// <summary>
    /// 帧体就是下发体
    /// </summary>
    public override string BuildFrame()
    {
        return Wire;
    }
}
