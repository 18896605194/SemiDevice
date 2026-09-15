namespace xyz.Drivers.Loadport;

public sealed class LoadPortResponse
{
    public bool IsSuccess { get; init; }

    public string Error { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public IReadOnlyList<SlotState> SlotMap { get; init; } = Array.Empty<SlotState>();

    public LoadPortStatus? Status { get; init; }
}
