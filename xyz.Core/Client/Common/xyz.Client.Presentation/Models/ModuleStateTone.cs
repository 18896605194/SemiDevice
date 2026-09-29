namespace xyz.Client.Presentation.Models;

/// <summary>
/// 模块状态色调：状态徽标（ModuleStateBadge）按它上底色。
/// 各模块的显示模型按自己的状态码归类——同一个码在不同模块意思不同（LoadPort 110 已装载 / 腔体 110 工艺中），
/// 所以不能在控件里按码配色。
/// </summary>
public enum ModuleStateTone
{
    /// <summary>灰：未初始化、未知码。</summary>
    Inactive,

    /// <summary>蓝：动作中（初始化、回零、取放、装卸、传片环、工艺……），这会儿不能再派活。</summary>
    Busy,

    /// <summary>绿：就绪，可以被服务（空闲、LoadPort 已装载）。</summary>
    Ready,

    /// <summary>黄：中止中。</summary>
    Warning,

    /// <summary>红：报错。</summary>
    Alarm,
}
