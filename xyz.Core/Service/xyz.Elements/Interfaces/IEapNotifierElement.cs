namespace xyz.Elements.Interfaces;

/// <summary>
/// EAP 上报口
/// </summary>
public interface IEapNotifierElement
{
    /// <summary>
    /// 排进上报线程去做
    /// </summary>
    void Post(Action notification);
}
