namespace xyz.Elements.Interfaces;

/// <summary>
/// 阀组：下面挂的阀一起开、一起关
/// </summary>
public interface IValveGroup : IDevice
{
    bool Open();

    bool Close();
}
