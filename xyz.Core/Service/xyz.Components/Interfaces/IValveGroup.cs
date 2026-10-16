namespace xyz.Components.Interfaces;

/// <summary>
/// 阀组：下面挂的阀一起开、一起关
/// </summary>
public interface IValveGroup : IDeviceComponent
{
    bool Open();

    bool Close();
}
