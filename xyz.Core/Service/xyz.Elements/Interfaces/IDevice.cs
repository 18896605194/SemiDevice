using xyz.Elements.Enums;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 设备元件的公共接口
/// </summary>
public interface IDevice
{
    /// <summary>
    /// 元件实例名
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 元件在设备树里的完整路径
    /// </summary>
    string FullPath { get; }

    /// <summary>
    /// 当前动作的状态
    /// </summary>
    ActionState ActionState { get; }
}

