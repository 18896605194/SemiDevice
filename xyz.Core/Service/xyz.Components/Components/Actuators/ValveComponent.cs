using xyz.Components.Attributes;

using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// 阀门组件：单 DO 驱动
/// </summary>
[Component(description: "阀门组件 (单 DO 驱动，NC / NO 可选；到位反馈挂子节点 Feedback，可选)")]
public class ValveComponent : OneStateComponent, IValve
{
    public bool Open()
    {
        return On();
    }

    public bool Close()
    {
        return Off();
    }

    public bool IsOpened => IsOn;
}
