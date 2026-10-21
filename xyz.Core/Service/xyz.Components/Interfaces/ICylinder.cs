using xyz.Components.Enums;

namespace xyz.Components.Interfaces;

/// <summary>
/// 气缸
/// </summary>
public interface ICylinder : IDevice
{
    bool IsOpened { get; }

    bool IsClosed { get; }

    TwoStatePosition Position { get; }

    bool Open();

    bool Close();
}

