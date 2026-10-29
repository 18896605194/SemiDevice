using xyz.Elements.Interfaces;
using xyz.Elements.Models;

namespace xyz.Elements.Elements;

/// <summary>
/// 警告报警的平台默认策略：报出来就完，不做别的
/// </summary>
public class WarnAlarmStrategy : IAlarmStrategy
{
    /// <summary>
    /// 报出来那一下做的事：警告不影响生产，这里什么都不做
    /// </summary>
    public virtual void OnRaised(AlarmContext context)
    {
    }
}
