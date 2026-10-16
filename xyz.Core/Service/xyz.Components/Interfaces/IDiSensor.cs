namespace xyz.Components.Interfaces;


public interface IDiSensor : IDeviceComponent
{
    /// <summary>
    /// 有没有触发
    /// </summary>
    bool? IsActive { get; }
}
