using xyz.Elements.Interfaces;
using xyz.Elements.Models;

namespace xyz.Elements.Elements;

/// <summary>
/// 致命报警的平台默认策略：machine.xml 只能配它或它的子类
/// </summary>
public class FatalAlarmStrategy : IAlarmStrategy
{
    /// <summary>
    /// 报出来那一下做的事：具体做什么还没定，机型要加东西就继承它重写
    /// </summary>
    public virtual void OnRaised(AlarmContext context)
    {
    }
}
