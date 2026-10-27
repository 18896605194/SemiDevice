namespace xyz.Elements.Interfaces;

/// <summary>
/// robot的轴不包含arm
/// </summary>
public interface IRobotAxis : IDevice
{
    double? Position { get; }

    void UpdatePosition(double? position);
}
