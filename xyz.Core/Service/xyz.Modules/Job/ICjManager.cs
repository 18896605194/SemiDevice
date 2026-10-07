using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Shared.Dtos;

namespace xyz.Modules;


public interface ICjManager
{
    event Action<ControlJob, int>? StateChanged;

    IReadOnlyList<ControlJob> Jobs { get; }

    ControlJob? Find(string id);

    ControlJob? FindByLoadPort(string loadPort);

    HandleResult Create(ControlJobSpec spec);

    /// <summary>执行 CJ 命令（E94 CJStart / CJPause / CJResume / CJCancel / CJDeselect / CJStop / CJAbort / CJHOQ）。</summary>
    HandleResult Execute(string id, ControlJobCommand command, ControlJobAction action);

    /// <summary>整机停止：没结束的 CJ 都走中止（排队的 PJ 一起删）。</summary>
    void AbortAll();

    /// <summary>把每个 CJ 该自动转的转一轮；转了返回 true。LoadPort 上的载具好没好、拿没拿走由 Job 组件查设备给。</summary>
    bool Advance(Func<string, bool> isCarrierReady, Func<ControlJob, bool> isCarrierGone);
}
