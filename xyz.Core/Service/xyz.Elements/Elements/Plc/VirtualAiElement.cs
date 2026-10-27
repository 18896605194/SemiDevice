using xyz.Elements.Attributes;

namespace xyz.Elements.Elements;

[Element(description: "虚拟 AI：一个带固定索引的数值表达式计算点")]
public class VirtualAiElement : VirtualInputElement
{
    internal override string PointType => "AI";

    /// <summary>最近一次工程值计算结果；null 表示来源无效、除零或数值溢出。</summary>
    public double? Value => CurrentValue;

    public bool TryRead(out double value) => TryEvaluate(out value);
}
