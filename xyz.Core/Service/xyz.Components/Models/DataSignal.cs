namespace xyz.Components.Models;


public sealed record DataSignal(
    string Name,
    bool IsDigital,
    string Source,
    string Address,
    string Unit,
    string Description,
    Func<double?> Read);
