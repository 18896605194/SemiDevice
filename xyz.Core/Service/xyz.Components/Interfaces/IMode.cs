using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 模式
/// </summary>
public interface IMode
{
    /// <summary>
    ModuleMode Mode { get; }

    void SetProduction();

    void SetMaintenance();
}
