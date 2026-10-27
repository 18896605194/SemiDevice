using xyz.Elements.Attributes;

using xyz.Elements.Interfaces;

namespace xyz.Elements.Elements;

/// <summary>
/// 阀门元件：单 DO 驱动
/// </summary>
[Element(description: "阀门元件 (单 DO 驱动，NC / NO 可选；到位反馈挂子节点 Feedback，可选)")]
public class ValveElement : OneStateElement, IValve
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
