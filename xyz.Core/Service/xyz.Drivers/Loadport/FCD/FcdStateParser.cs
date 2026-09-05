namespace xyz.Drivers.Loadport.FCD;

/// <summary>
/// FCD 64 字符 GET:STATE 状态串 → 标准状态快照（LoadPortStatus）。
/// 已确认位：byte1 在位 / byte2 放好 / byte8 硬件报警（'1'=true），
/// byte43 门开 / byte44 门关（'O'=到位）；其余位待协议手册确认。
/// </summary>
public static class FcdStateParser
{
    private const int PodPresentIndex = 0;
    private const int PodPlacedIndex = 1;
    private const int DeviceAlarmIndex = 7;
    private const int DoorOpenIndex = 42;
    private const int DoorClosedIndex = 43;
    private const int StateLength = 44;

    public static LoadPortStatus Parse(string state)
    {
        if (state.Length < StateLength)
        {
            throw new ArgumentException($"状态串长度不足: {state.Length}/{StateLength}", nameof(state));
        }

        return new LoadPortStatus
        {
            PodPresent = state[PodPresentIndex] == '1',
            PodPlaced = state[PodPlacedIndex] == '1',
            DeviceAlarm = state[DeviceAlarmIndex] == '1',
            DoorOpen = state[DoorOpenIndex] == 'O',
            DoorClosed = state[DoorClosedIndex] == 'O',
            Raw = state,
        };
    }
}
