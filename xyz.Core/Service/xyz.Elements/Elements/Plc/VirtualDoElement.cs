using xyz.Elements.Attributes;
using xyz.Elements.Interfaces;
using xyz.Configs.Models;

namespace xyz.Elements.Elements;

/// <summary>
/// 虚拟 DO：挂在虚拟 IO 下面，声明一个点号；读写走虚拟 IO
/// </summary>
[Element(description: "虚拟 DO：挂在虚拟 IO 下面，声明一个点号（5000~5999）；按点号读写走虚拟 IO")]
public class VirtualDoElement : ElementBase, IVirtualDo
{
    /// <summary>
    /// 虚拟 DO 点号从这里起
    /// </summary>
    public const int FirstIndex = 5000;

    /// <summary>
    /// 虚拟 DO 点号到这里止
    /// </summary>
    public const int LastIndex = 5999;

    #region Machine

    [Machine("-1", "VirtualOutput", "全局唯一点号：虚拟 DO 5000~5999", Required = true)]
    public int Index { get; set; } = -1;

    #endregion

    /// <summary>
    /// 装配校验：点号不在 5000~5999 开机就报
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);
        if (Index < FirstIndex || Index > LastIndex)
        {
            throw new InvalidOperationException($"{Name}：虚拟 DO 点号必须在 {FirstIndex}~{LastIndex}，现在是 {Index}");
        }
    }
}
