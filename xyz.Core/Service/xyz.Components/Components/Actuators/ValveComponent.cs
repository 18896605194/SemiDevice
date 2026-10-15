using xyz.Components.Attributes;

using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// 阀门组件：单 DO 驱动
/// </summary>
[Component(description: "阀门组件 (单 DO 驱动，可选到位反馈)")]
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
