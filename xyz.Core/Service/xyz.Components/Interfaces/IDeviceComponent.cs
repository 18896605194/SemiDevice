using xyz.Components.Enums;

namespace xyz.Components.Interfaces;

/// <summary>设备组件的公共信息：名称和路径标识实例，动作结果由组件自己判断。</summary>
public interface IDeviceComponent
{
    /// <summary>组件实例名。</summary>
    string Name { get; }

    /// <summary>组件在设备树里的完整路径。</summary>
    string FullPath { get; }

    /// <summary>当前动作的完成、失败或在途状态。</summary>
    ActionState ActionState { get; }
}

