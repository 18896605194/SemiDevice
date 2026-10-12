using xyz.Components.Enums;

namespace xyz.Components.Interfaces;

/// <summary>气缸的两侧反馈与动作；接线、到位和超时由实现组件负责。</summary>
public interface ICylinder : IDeviceComponent
{
    /// <summary>开侧是否到位。</summary>
    bool IsOpened { get; }

    /// <summary>关侧是否到位。</summary>
    bool IsClosed { get; }

    /// <summary>开到位、关到位或位置未知。</summary>
    TwoStatePosition Position { get; }

    /// <summary>向开侧运动。</summary>
    bool Open();

    /// <summary>向关侧运动。</summary>
    bool Close();
}

