using xyz.Drivers.Loadport;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

public interface ICarrier
{

    bool IsArrived { get; }

    string? CarrierId { get; }

    IReadOnlyList<SlotState> SlotMap { get; }

    bool ReadId();

    void SetId(string carrierId);

    void UpdateStatus(CarrierIdStatus? idStatus, CarrierSlotMapStatus? slotMapStatus);

    void NoteComplete();
}
