namespace xyz.Components.Interfaces;

/// <summary>
/// robot的轴不包含arm
/// </summary>
public interface IRobotAxis : IDeviceComponent
{
    double? Position { get; }

    void UpdatePosition(double? position);
}
