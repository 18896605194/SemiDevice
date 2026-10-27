namespace xyz.Elements.Interfaces;

/// <summary>
/// AI 液位计：挂在槽的液位点上，几个点读的是同一个 AI
/// </summary>
public interface IAiLevel : IDiSensor
{
    /// <summary>
    /// 读的哪个 AI
    /// </summary>
    int AiIndex { get; }
}
