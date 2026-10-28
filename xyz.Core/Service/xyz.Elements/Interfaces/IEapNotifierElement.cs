using xyz.Elements.Attributes;

namespace xyz.Elements.Interfaces;

/// <summary>
/// EAP 上报口
/// </summary>
[Ioc]
public interface IEapNotifierElement
{
    /// <summary>
    /// 排进上报线程去做
    /// </summary>
    void Post(Action notification);
}
