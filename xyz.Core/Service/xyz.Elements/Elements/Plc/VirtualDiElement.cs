using xyz.Elements.Attributes;

namespace xyz.Elements.Elements;

[Element(description: "虚拟 DI：一个带固定索引的布尔表达式计算点")]
public class VirtualDiElement : VirtualInputElement
{
    internal override string PointType => "DI";

    /// <summary>最近一次计算结果；null 表示来源无效或计算失败。</summary>
    public bool? Value => CurrentValue is double value ? value != 0 : null;

    public bool TryRead(out bool value)
    {
        bool valid = TryEvaluate(out double result);
        value = valid && result != 0;
        return valid;
    }
}
