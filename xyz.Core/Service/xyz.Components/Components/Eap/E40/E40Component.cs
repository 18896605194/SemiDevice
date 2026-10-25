using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Secs.Gem;
using xyz.Secs.Gem.E40;
using xyz.Shared.Dtos;

namespace xyz.Components.Components;

/// <summary>
/// PJ 管理（SEMI E40，machine.xml 的 Eap 下的 E40 节点）：S16 的协议在 xyz.Secs 的 E40Engine，这里给它 Job 管理（EapJobs）、
/// machine.xml 的命令字符串，Job 管理报上来的 PJ 状态转换交给它报事件
/// </summary>
[Component(description: "PJ 管理（SEMI E40）：S16 建 PJ、PJ 命令、查 PJ，PJ 状态转换报事件")]
public class E40Component : ComponentBase, IE40Callback, IE40Component, IE40Settings, IGemEvents
{
    #region Machine

    [Machine("START", "E40", "S16F5 的 PJ 启动命令字符串（忽略大小写及首尾空格；不能重复或占用其他命令的默认名称）")]
    public string StartCommandName { get; set; } = "START";

    [Machine("PAUSE", "E40", "S16F5 的 PJ 暂停命令字符串")]
    public string PauseCommandName { get; set; } = "PAUSE";

    [Machine("RESUME", "E40", "S16F5 的 PJ 恢复命令字符串")]
    public string ResumeCommandName { get; set; } = "RESUME";

    [Machine("STOP", "E40", "S16F5 的 PJ 停止命令字符串")]
    public string StopCommandName { get; set; } = "STOP";

    [Machine("ABORT", "E40", "S16F5 的 PJ 中止命令字符串")]
    public string AbortCommandName { get; set; } = "ABORT";

    [Machine("CANCEL", "E40", "S16F5 的 PJ 取消命令字符串")]
    public string CancelCommandName { get; set; } = "CANCEL";

    #endregion

    /// <summary>
    /// 装配校验：命令字符串不能为空、不能互相重复、不能占用别的命令的默认名称，配错了开机就抛
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);
        string? error = E40Engine.CheckCommandNames(this);
        if (error is not null)
        {
            throw new InvalidOperationException($"{Name}：{error}");
        }
    }

    #region DV、事件

    [DataVariable(ValueFormat.String, "PJ 号")]
    public readonly string JobIdData = E40Codes.JobId;

    [DataVariable(ValueFormat.Int, "PJ 状态：0 排队、1 准备、2 等启动、3 在做、4 做完、6 暂停中、7 暂停、8 停止中、9 中止中、10 停止、11 中止")]
    public readonly string JobStateData = E40Codes.JobState;

    [DataVariable(ValueFormat.String, "PJ 的配方（流程配方名）")]
    public readonly string RecipeData = E40Codes.Recipe;

    [DataVariable(ValueFormat.String, "PJ 归哪个 CJ（还不归任何 CJ 为空）")]
    public readonly string ControlJobData = E40Codes.ControlJob;

    [DataVariable(ValueFormat.String, "PJ 的料：L{L[2]{载具号, L{槽号}}}")]
    public readonly string MaterialData = E40Codes.Material;

    [EventAttribut("PJ 建了（#1）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.Recipe, E40Codes.Material })]
    public readonly string PrJobTrans01 = "PrJobSMTrans01";

    [EventAttribut("PJ 开始准备（#2）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.ControlJob })]
    public readonly string PrJobTrans02 = "PrJobSMTrans02";

    [EventAttribut("PJ 等启动（#3）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.ControlJob })]
    public readonly string PrJobTrans03 = "PrJobSMTrans03";

    [EventAttribut("PJ 准备好直接开始（#4）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.ControlJob })]
    public readonly string PrJobTrans04 = "PrJobSMTrans04";

    [EventAttribut("PJ 启动（#5）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.ControlJob })]
    public readonly string PrJobTrans05 = "PrJobSMTrans05";

    [EventAttribut("PJ 片都做完了（#6）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.ControlJob })]
    public readonly string PrJobTrans06 = "PrJobSMTrans06";

    [EventAttribut("PJ 正常结束（#7）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.ControlJob })]
    public readonly string PrJobTrans07 = "PrJobSMTrans07";

    [EventAttribut("PJ 暂停中（#8）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.ControlJob })]
    public readonly string PrJobTrans08 = "PrJobSMTrans08";

    [EventAttribut("PJ 暂停了（#9）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.ControlJob })]
    public readonly string PrJobTrans09 = "PrJobSMTrans09";

    [EventAttribut("PJ 恢复（#10）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.ControlJob })]
    public readonly string PrJobTrans10 = "PrJobSMTrans10";

    [EventAttribut("PJ 停止中（#11，从执行中）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.ControlJob })]
    public readonly string PrJobTrans11 = "PrJobSMTrans11";

    [EventAttribut("PJ 停止中（#12，从暂停）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.ControlJob })]
    public readonly string PrJobTrans12 = "PrJobSMTrans12";

    [EventAttribut("PJ 中止中（#13，从执行中）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.ControlJob })]
    public readonly string PrJobTrans13 = "PrJobSMTrans13";

    [EventAttribut("PJ 中止中（#14，从停止中）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.ControlJob })]
    public readonly string PrJobTrans14 = "PrJobSMTrans14";

    [EventAttribut("PJ 中止中（#15，从暂停）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.ControlJob })]
    public readonly string PrJobTrans15 = "PrJobSMTrans15";

    [EventAttribut("PJ 中止完结束（#16）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.ControlJob })]
    public readonly string PrJobTrans16 = "PrJobSMTrans16";

    [EventAttribut("PJ 停止完结束（#17）", Data = new[] { E40Codes.JobId, E40Codes.JobState, E40Codes.ControlJob })]
    public readonly string PrJobTrans17 = "PrJobSMTrans17";

    [EventAttribut("排队的 PJ 删了（#18）", Data = new[] { E40Codes.JobId, E40Codes.JobState })]
    public readonly string PrJobTrans18 = "PrJobSMTrans18";

    #endregion

    private IJobManager? _jobs;
    private E40Engine? _engine;

    #region 接设备

    /// <summary>
    /// 接到链路、GEM、对象服务和 Job 管理上（EAP 组件在链路打开之前调）：登记 S16、E39 对象类型 ProcessJob，挂 E40 上报口
    /// </summary>
    public void Attach(IHsms link, IE30Component gem, IE39Component? objects, IJobManager jobs)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(gem);
        ArgumentNullException.ThrowIfNull(jobs);
        var engine = gem.Engine;
        if (engine is null)
        {
            throw new InvalidOperationException($"{Name}：E30 还没接链路");
        }

        _jobs = jobs;
        _engine = new E40Engine(link.Link, engine, objects?.Engine, new EapJobs(jobs), this, this, new SecsLog(this), Name);
        jobs.E40Callback = this;
    }

    /// <summary>
    /// 从 Job 管理上摘下来（宿主退出时）
    /// </summary>
    public void Detach()
    {
        var jobs = _jobs;
        if (jobs is not null && ReferenceEquals(jobs.E40Callback, this))
        {
            jobs.E40Callback = null;
        }

        _engine = null;
    }

    #endregion

    #region 上报（IE40Callback，在 EAP 的上报派发线程上）

    public void ProcessJobStateChanged(ProcessJobDto job, int transition)
    {
        _engine?.StateChanged(EapJobs.ProcessJobOf(job), transition);
    }

    void IGemEvents.Report(string code, params (string Name, object? Value)[] data)
    {
        RaiseEvent(code, data.Select(item => new GemData(item.Name, item.Value)).ToArray());
    }

    #endregion
}
