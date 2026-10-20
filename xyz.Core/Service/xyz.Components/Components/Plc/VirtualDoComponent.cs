using xyz.Components.Attributes;
using xyz.Components.Interfaces;
using xyz.Configs.Models;

namespace xyz.Components.Components;

/// <summary>
/// 虚拟 DO：一个带固定点号的程序输出点，经 IO 组件按点号读写，只在内存里、不下发 PLC
/// </summary>
[Component(description: "虚拟 DO：一个带固定点号的程序输出点（只在内存里，不下发 PLC），经 IO 组件按点号读写")]
public class VirtualDoComponent : ComponentBase, IVirtualDo
{
    /// <summary>
    /// 虚拟 DO 点号从这里起
    /// </summary>
    public const int FirstIndex = 5000;

    /// <summary>
    /// 虚拟 DO 点号到这里止
    /// </summary>
    public const int LastIndex = 5999;

    #region SC

    [SCEditor("-1", "VirtualOutput", "全局唯一点号：虚拟 DO 5000~5999", Required = true)]
    public int Index { get; set; } = -1;

    #endregion

    private volatile bool _value;

    /// <summary>
    /// 现在是通还是断；开机是断
    /// </summary>
    public bool Value
    {
        get { return _value; }
    }

    public void Write(bool on)
    {
        _value = on;
    }

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
