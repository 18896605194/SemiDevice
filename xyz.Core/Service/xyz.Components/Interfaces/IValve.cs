namespace xyz.Components.Interfaces;

public interface IValve : IDevice
{
    bool IsOpened { get; }

    bool Open();

    bool Close();
}

