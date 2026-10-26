using System.Diagnostics;

namespace xyz.Service;

/// <summary>
/// 方法调用
/// </summary>
public sealed class ActionCall
{
    public ActionCall(string who)
    {
        Who = who;
    }

    /// <summary>
    /// "操作人
    /// </summary>
    public string Who { get; }

    /// <summary>
    /// 从进来开始的计时，回包时算用时
    /// </summary>
    public Stopwatch Watch { get; } = Stopwatch.StartNew();
}
