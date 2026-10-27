using xyz.Components.Enums;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

public interface ITank : IDevice
{
    TankLevel Level { get; }

    TankState State { get; }

    /// <summary>
    /// 模式：只有一个槽的一直是 Supply；两个槽的在工艺过程中由上面的模组对调，不在 machine.xml 配
    /// </summary>
    TankMode Mode { get; set; }

    HandleResult Fill();

    HandleResult Drain();

    HandleResult Exchange();

    HandleResult Supply();

    HandleResult Stop();
}
