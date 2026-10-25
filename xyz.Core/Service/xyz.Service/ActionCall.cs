using System.Diagnostics;

namespace xyz.Service;

/// <summary>
/// 一次要记的调用：谁@哪台电脑 调了什么，和从进来开始的计时（IActionFilter.Begin 建，End / Fault 用）
/// </summary>
public sealed class ActionCall
{
    public ActionCall(string who)
    {
        Who = who;
    }

    /// <summary>
    /// "操作人@IP 服务.方法"，比如 张三@192.168.1.20 LoadPort.Load
    /// </summary>
    public string Who { get; }

    /// <summary>
    /// 从进来开始的计时，回包时算用时
    /// </summary>
    public Stopwatch Watch { get; } = Stopwatch.StartNew();
}
