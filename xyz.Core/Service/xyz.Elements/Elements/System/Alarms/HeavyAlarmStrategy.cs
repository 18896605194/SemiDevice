using xyz.Elements.Interfaces;
using xyz.Elements.Models;

namespace xyz.Elements.Elements;

/// <summary>
/// 重报警的平台默认策略
/// </summary>
public class HeavyAlarmStrategy : IAlarmStrategy
{
    /// <summary>
    /// 报出来那一下做的事：具体做什么还没定，机型要自己的做法就继承它重写
    /// </summary>
    public virtual void OnRaised(AlarmContext context)
    {
    }
}
