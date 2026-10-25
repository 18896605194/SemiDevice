namespace xyz.Components.Components;

/// <summary>
/// E164 用到的几个 EAP 标准组件（没装的为 null）：元数据按装了哪些带上对应的状态机、对象类型，事件按它们报的翻成 E164 的标准事件。
/// EAP 链路没启用时这些组件不接设备、不会报事件，就全当没装
/// </summary>
internal sealed class EdaE164Sources
{
    /// <summary>
    /// GEM（控制状态）
    /// </summary>
    public E30Component? E30 { get; init; }

    /// <summary>
    /// 配方管理（ProcessProgramChange）
    /// </summary>
    public E30RecipeComponent? Recipe { get; init; }

    /// <summary>
    /// PJ
    /// </summary>
    public E40Component? E40 { get; init; }

    /// <summary>
    /// CJ
    /// </summary>
    public E94Component? E94 { get; init; }

    /// <summary>
    /// 载具
    /// </summary>
    public E87Component? E87 { get; init; }

    /// <summary>
    /// 片和片位
    /// </summary>
    public E90Component? E90 { get; init; }
}
