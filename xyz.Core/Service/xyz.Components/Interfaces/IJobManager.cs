using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

public interface IJobManager
{
    /// <summary>建一个 PJ（E40）：Host 给载具号，本地给 LoadPort。建好排着，等 CJ 来收。</summary>
    Task<HandleResult> CreateProcessJobAsync(ProcessJobSpec spec);

    /// <summary>建一个 CJ（E94），把已经建好、还没归 CJ 的 PJ 按顺序收进来。</summary>
    Task<HandleResult> CreateControlJobAsync(ControlJobSpec spec);

    /// <summary>CJ 命令（E94 CJStart / CJPause / CJResume / CJCancel / CJDeselect / CJStop / CJAbort / CJHOQ）。</summary>
    Task<HandleResult> ExecuteControlJobCommandAsync(string id, ControlJobCommand command, ControlJobAction action);

    /// <summary>PJ 命令（E40 Start / Pause / Resume / Stop / Abort / Cancel）。</summary>
    Task<HandleResult> ExecuteProcessJobCommandAsync(string id, ProcessJobCommand command);

    ControlJobDto? FindControlJobByCarrier(string carrierId);
    IReadOnlyList<ProcessJobDto> FindProcessJobsByCarrier(string carrierId);

    JobListDto Snapshot { get; }

    IE40Callback? E40Callback { get; set; }

    IE94Callback? E94Callback { get; set; }

}
