namespace xyz.Components.Interfaces;

/// <summary>阀门的通断与反馈，不向模块暴露 IO 接线。</summary>
public interface IValve : IDeviceComponent
{
    /// <summary>阀是否开到位。</summary>
    bool IsOpened { get; }

    /// <summary>开阀。</summary>
    bool Open();

    /// <summary>关阀。</summary>
    bool Close();
}

