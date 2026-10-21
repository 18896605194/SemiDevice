using xyz.Components.Enums;

namespace xyz.Components.Interfaces;

/// <summary>
/// 设备组件的公共接口
/// </summary>
public interface IDevice
{
    /// <summary>
    /// 组件实例名
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 组件在设备树里的完整路径
    /// </summary>
    string FullPath { get; }

    /// <summary>
    /// 当前动作的状态
    /// </summary>
    ActionState ActionState { get; }
}

