namespace xyz.Components.Interfaces;

public interface IValve : IDeviceComponent
{
    bool IsOpened { get; }

    bool Open();

    bool Close();
}

