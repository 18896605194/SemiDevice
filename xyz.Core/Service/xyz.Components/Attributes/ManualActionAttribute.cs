using System;

namespace xyz.Components.Attributes;

/// <summary>
/// 手动页能做的动作：标在组件的公开方法上，界面按"组件路径 + 方法名 + 参数"调，模块统一把关（模块正忙不发、做完回原来的状态）。
/// 方法返回 bool：true 只表示指令发出去了，做没做完看组件的 <see cref="ComponentBase.ActionState"/>。
/// 参数按方法签名从字符串转（不变区域性），可选参数不给就用默认值。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ManualActionAttribute : Attribute
{
    /// <summary>
    /// 停止类：模块正忙（别的动作在途）也照发、不等结果，比如轴停止。
    /// </summary>
    public bool Priority { get; set; }

    /// <summary>
    /// 按住类动作（点动）松手时发的动作名，比如 Jog 的 Release = "Stop"；不为空就是按住类：
    /// 发起后界面按住期间一直续，松手发这个动作；续不上（界面断了、客户端退了）模块自己发。
    /// </summary>
    public string? Release { get; set; }
}
