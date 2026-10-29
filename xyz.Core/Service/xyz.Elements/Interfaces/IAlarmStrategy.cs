using xyz.Elements.Models;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 报警处理策略
/// </summary>
public interface IAlarmStrategy
{
    /// <summary>
    /// 报出来那一下做的事：只发请求、不等结果，平台放到模块扫描线程里执行
    /// </summary>
    void OnRaised(AlarmContext context);
}
