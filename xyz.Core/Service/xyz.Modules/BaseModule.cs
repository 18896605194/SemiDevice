using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 模块：状态表、挂动作在 <see cref="BaseStateComponent"/>，这里只放模块才有的
/// </summary>
public abstract class BaseModule : BaseStateComponent, IModule
{
    /// <summary>
    /// 模块是哪一类：LoadPort、腔体、buffer、机械手的基类各自重写，别的模块默认其他
    /// </summary>
    public virtual StationKind Kind => StationKind.Other;

    /// <summary>
    /// 站点的类型（sc.xml 里写成 <c>&lt;Value Name="Type" Value="A" /&gt;</c>，不是节点上写类名的那个 Type 属性）：
    /// 几台一样的站点写同一个值（比如 8 个腔体 1~4 写 A、5~8 写 B），工艺配方按它定给哪种站点用。空 = 不分类型。
    /// </summary>
    [Machine("", "Module", "站点类型：几台一样的写同一个值（比如腔体 1~4 写 A、5~8 写 B），工艺配方按它挑能跑的站点；空 = 不分类型")]
    public string Type { get; set; } = string.Empty;

    public virtual ModuleRoutine? InitModule()
    {
        return null;
    }

    /// <summary>
    /// 复位：子组件跟着复位、清自己的报警；模块有自己的复位动作就重写
    /// </summary>
    public override ModuleRoutine? Reset()
    {
        base.Reset();
        return null;
    }

    /// <summary>
    /// 中止：子组件跟着中止；模块有自己的中止动作就重写
    /// </summary>
    public override ModuleRoutine? Abort()
    {
        base.Abort();
        return null;
    }
}
