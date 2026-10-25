using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Secs.Gem;
using xyz.Secs.Gem.E94;
using xyz.Shared.Dtos;

namespace xyz.Components.Components;

/// <summary>
/// CJ 管理（SEMI E94，machine.xml 的 Eap 下的 E94 节点）：S14F9 建 CJ、S16F27 CJ 命令的协议在 xyz.Secs 的 E94Engine，
/// 这里给它 Job 管理（EapJobs）、machine.xml 的命令字符串，Job 管理报上来的 CJ 状态转换交给它报事件
/// </summary>
[Component(description: "CJ 管理（SEMI E94）：S14F9 建 CJ、S16F27 CJ 命令，CJ 状态转换报事件")]
public class E94Component : ComponentBase, IE94Callback, IE94Component, IE94Settings, IGemEvents
{
    #region Machine

    [Machine("CJSTART", "E94", "S16F27 的 CJ 启动命令字符串（忽略大小写及首尾空格；不能重复或占用其他命令的默认名称）")]
    public string StartCommandName { get; set; } = "CJSTART";

    [Machine("CJPAUSE", "E94", "S16F27 的 CJ 暂停命令字符串")]
    public string PauseCommandName { get; set; } = "CJPAUSE";

    [Machine("CJRESUME", "E94", "S16F27 的 CJ 恢复命令字符串")]
    public string ResumeCommandName { get; set; } = "CJRESUME";

    [Machine("CJCANCEL", "E94", "S16F27 的 CJ 取消命令字符串")]
    public string CancelCommandName { get; set; } = "CJCANCEL";

    [Machine("CJDESELECT", "E94", "S16F27 的 CJ 取消选中命令字符串")]
    public string DeselectCommandName { get; set; } = "CJDESELECT";

    [Machine("CJSTOP", "E94", "S16F27 的 CJ 停止命令字符串")]
    public string StopCommandName { get; set; } = "CJSTOP";

    [Machine("CJABORT", "E94", "S16F27 的 CJ 中止命令字符串")]
    public string AbortCommandName { get; set; } = "CJABORT";

    [Machine("CJHOQ", "E94", "S16F27 的 CJ 插到队首命令字符串（Job 管理当前不支持执行此命令）")]
    public string HeadOfQueueCommandName { get; set; } = "CJHOQ";

    #endregion

    /// <summary>
    /// 装配校验：命令字符串不能为空、不能互相重复、不能占用别的命令的默认名称，配错了开机就抛（运行时认命令另有一层冲突拒绝兜底）
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);
        string? error = E94Engine.CheckCommandNames(this);
        if (error is not null)
        {
            throw new InvalidOperationException($"{Name}：{error}");
        }
    }

    #region DV、事件

    [DataVariable(ValueFormat.String, "CJ 号")]
    public readonly string JobIdData = E94Codes.JobId;

    [DataVariable(ValueFormat.Int, "CJ 状态：0 排队、1 选中、2 等启动、3 在跑、4 暂停、5 完成")]
    public readonly string JobStateData = E94Codes.JobState;

    [DataVariable(ValueFormat.String, "CJ 的载具号")]
    public readonly string CarrierIdData = E94Codes.CarrierId;

    [DataVariable(ValueFormat.String, "CJ 下的 PJ（L{PRJOBID}，按执行先后）")]
    public readonly string ProcessJobsData = E94Codes.ProcessJobs;

    [EventAttribut("CJ 建了（#1）", Data = new[] { E94Codes.JobId, E94Codes.JobState, E94Codes.CarrierId, E94Codes.ProcessJobs })]
    public readonly string CtrlJobTrans01 = "CtrlJobSMTrans01";

    [EventAttribut("排队的 CJ 删了（#2）", Data = new[] { E94Codes.JobId, E94Codes.JobState })]
    public readonly string CtrlJobTrans02 = "CtrlJobSMTrans02";

    [EventAttribut("CJ 选中（#3）", Data = new[] { E94Codes.JobId, E94Codes.JobState, E94Codes.CarrierId })]
    public readonly string CtrlJobTrans03 = "CtrlJobSMTrans03";

    [EventAttribut("CJ 退回排队（#4）", Data = new[] { E94Codes.JobId, E94Codes.JobState })]
    public readonly string CtrlJobTrans04 = "CtrlJobSMTrans04";

    [EventAttribut("CJ 料到了直接开始（#5）", Data = new[] { E94Codes.JobId, E94Codes.JobState, E94Codes.CarrierId })]
    public readonly string CtrlJobTrans05 = "CtrlJobSMTrans05";

    [EventAttribut("CJ 料到了等启动（#6）", Data = new[] { E94Codes.JobId, E94Codes.JobState, E94Codes.CarrierId })]
    public readonly string CtrlJobTrans06 = "CtrlJobSMTrans06";

    [EventAttribut("CJ 启动（#7）", Data = new[] { E94Codes.JobId, E94Codes.JobState })]
    public readonly string CtrlJobTrans07 = "CtrlJobSMTrans07";

    [EventAttribut("CJ 暂停（#8）", Data = new[] { E94Codes.JobId, E94Codes.JobState })]
    public readonly string CtrlJobTrans08 = "CtrlJobSMTrans08";

    [EventAttribut("CJ 恢复（#9）", Data = new[] { E94Codes.JobId, E94Codes.JobState })]
    public readonly string CtrlJobTrans09 = "CtrlJobSMTrans09";

    [EventAttribut("CJ 正常完成（#10）", Data = new[] { E94Codes.JobId, E94Codes.JobState, E94Codes.CarrierId })]
    public readonly string CtrlJobTrans10 = "CtrlJobSMTrans10";

    [EventAttribut("CJ 停止完成（#11）", Data = new[] { E94Codes.JobId, E94Codes.JobState, E94Codes.CarrierId })]
    public readonly string CtrlJobTrans11 = "CtrlJobSMTrans11";

    [EventAttribut("CJ 中止完成（#12）", Data = new[] { E94Codes.JobId, E94Codes.JobState, E94Codes.CarrierId })]
    public readonly string CtrlJobTrans12 = "CtrlJobSMTrans12";

    [EventAttribut("完成的 CJ 删了（#13，载具拿走）", Data = new[] { E94Codes.JobId, E94Codes.JobState })]
    public readonly string CtrlJobTrans13 = "CtrlJobSMTrans13";

    #endregion

    private IJobManager? _jobs;
    private E94Engine? _engine;

    #region 接设备

    /// <summary>
    /// 接到链路和 Job 管理上（EAP 组件在链路打开之前调）：登记 S16F27 和 E39 对象类型 ControlJob（S14F9 建 CJ 走它），挂 E94 上报口。
    /// 没配 E39 时 Host 建不了 CJ，记错误
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
        _engine = new E94Engine(link.Link, engine, objects?.Engine, new EapJobs(jobs), this, this, new SecsLog(this), Name);
        jobs.E94Callback = this;
    }

    /// <summary>
    /// 从 Job 管理上摘下来（宿主退出时）
    /// </summary>
    public void Detach()
    {
        var jobs = _jobs;
        if (jobs is not null && ReferenceEquals(jobs.E94Callback, this))
        {
            jobs.E94Callback = null;
        }

        _engine = null;
    }

    #endregion

    #region 上报（IE94Callback）

    void IE94Callback.ControlJobStateChanged(ControlJobDto job, int transition)
    {
        _engine?.StateChanged(EapJobs.ControlJobOf(job), transition);
    }

    void IGemEvents.Report(string code, params (string Name, object? Value)[] data)
    {
        RaiseEvent(code, data.Select(item => new GemData(item.Name, item.Value)).ToArray());
    }

    #endregion
}
