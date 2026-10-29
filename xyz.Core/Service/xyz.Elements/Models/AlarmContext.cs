using xyz.Elements.Interfaces;

namespace xyz.Elements.Models;

/// <summary>
/// 报警上下文，给报警策略使用的
/// </summary>
public sealed class AlarmContext
{
    internal AlarmContext(AlarmItem alarm, IModule? module)
    {
        Alarm = alarm;
        Module = module;
    }

    /// <summary>
    /// 刚报出来的这条报警
    /// </summary>
    public AlarmItem Alarm { get; }

    /// <summary>
    /// 报警所属的模块
    /// </summary>
    public IModule? Module { get; }
}
