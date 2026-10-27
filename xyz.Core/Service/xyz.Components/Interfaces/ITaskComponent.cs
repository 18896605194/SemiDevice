using xyz.Components.Components;
using xyz.Components.Models;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 任务组件：Job 的任务表（一片一行）
/// </summary>
public interface ITaskComponent
{
    /// <summary>
    /// 任务表
    /// </summary>
    IReadOnlyList<TaskRow> Rows { get; }

    /// <summary>
    /// 任务开始了：记下站点、槽，取放还记机械手
    /// </summary>
    void Start(TaskRow row, WaferTask task, string station, int slot, string? robot = null, ModuleRoutine? routine = null);

    /// <summary>
    /// 任务做完了，取放记下用的哪只手
    /// </summary>
    void Done(TaskRow row, WaferTask task, int arm = 0);

    /// <summary>
    /// 任务出错：停在这一格等人处理
    /// </summary>
    void Fail(TaskRow row, WaferTask task, string code, IReadOnlyList<string> args);

    /// <summary>
    /// 退回等着做（没做成、也没碰到片）
    /// </summary>
    void Reset(WaferTask task);

    /// <summary>
    /// 不做了，记成未执行
    /// </summary>
    void Cancel(TaskRow row, WaferTask task);

    /// <summary>
    /// 给一个 PJ 建任务表（不留）
    /// </summary>
    internal HandleResult? Build(ProcessJob job);

    /// <summary>
    /// PJ 建好：它的行挂进任务表
    /// </summary>
    internal void Add(ProcessJob job);

    /// <summary>
    /// PJ 结束：没做的记未执行，行从任务表拿掉
    /// </summary>
    internal void Close(ProcessJob job);

    /// <summary>
    /// 把这个 PJ 的行标上中止
    /// </summary>
    internal void Abort(ProcessJob job);

    /// <summary>
    /// 片位被人改了没有
    /// </summary>
    internal void CheckPositions();

    /// <summary>
    /// 重做：出错的任务退回等着做
    /// </summary>
    internal HandleResult Retry(ProcessJob job, int slot, int index);

    /// <summary>
    /// 标记完成：人已经把这一步做完了
    /// </summary>
    internal HandleResult Complete(ProcessJob job, int slot, int index);
}
