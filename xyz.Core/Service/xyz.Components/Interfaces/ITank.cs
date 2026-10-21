using xyz.Components.Enums;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

public interface ITank : IDevice
{
    TankLevel Level { get; }

    TankState State { get; }

    HandleResult Fill();

    HandleResult Drain();

    HandleResult Exchange();

    HandleResult Supply();

    HandleResult Stop();
}
