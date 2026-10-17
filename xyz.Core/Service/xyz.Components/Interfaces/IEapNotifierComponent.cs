namespace xyz.Components.Interfaces;

/// <summary>
/// EAP 上报口
/// </summary>
public interface IEapNotifierComponent
{
    /// <summary>
    /// 排进上报线程去做
    /// </summary>
    void Post(Action notification);
}
