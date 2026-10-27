using xyz.Shared.Dtos;

namespace xyz.Elements.Interfaces;

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
