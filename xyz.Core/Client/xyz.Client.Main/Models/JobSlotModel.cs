using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.Main.Models;

/// <summary>
/// 主界面槽位表的一行：槽号、片号、状态（以晶圆账为准）+ 这片走哪个 Sequence。
/// Sequence 是界面上选的（⊕ 单独选、⊖ 清空、上面的 Sequence 框一次给全篮），Job 还没做，所以还不交给后端。
/// </summary>
public sealed class JobSlotModel : ObservableObject
{
    public JobSlotModel(int slot)
    {
        Slot = slot;
        SlotText = slot.ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>槽号，从 1 开始。</summary>
    public int Slot { get; }

    /// <summary>槽号显示两位（01、25）。</summary>
    public string SlotText { get; }

    private string _waferId = string.Empty;

    /// <summary>片号（晶圆账的业务片号）；空槽、账上没登记这个 LoadPort 时为空。</summary>
    public string WaferId
    {
        get => _waferId;
        private set => SetProperty(ref _waferId, value);
    }

    private JobWaferState _waferState;

    /// <summary>状态列显示什么（界面按它换字、换色）；空槽是 None。</summary>
    public JobWaferState WaferState
    {
        get => _waferState;
        private set
        {
            if (SetProperty(ref _waferState, value))
            {
                OnPropertyChanged(nameof(CanAssign));
            }
        }
    }

    /// <summary>
    /// 这一格能不能选 Sequence：有片，而且物理状态正常——交叉片、叠片、状态不明的片不做，⊕ ⊖ 也不显示。
    /// </summary>
    public bool CanAssign
    {
        get
        {
            switch (WaferState)
            {
                case JobWaferState.None:
                case JobWaferState.Crossed:
                case JobWaferState.Double:
                case JobWaferState.Unknown:
                    return false;
                default:
                    return true;
            }
        }
    }

    private string _sequence = string.Empty;

    /// <summary>这片走哪个 Sequence（流程配方名）；空 = 没选，这片不做。</summary>
    public string Sequence
    {
        get => _sequence;
        set => SetProperty(ref _sequence, value);
    }

    /// <summary>
    /// 刷新这一格的片。片没了、或者变得不能做了，Sequence 跟着清掉；
    /// 返回这一格是不是从"不能选"变成了"能选"（刚放上片、刚 Mapping 完），调用方据此给它套上 Sequence 框里选的那个。
    /// 换载具是先取走（全篮清空）再放上，所以新载具的片也走这一条。
    /// </summary>
    public bool Update(string waferId, JobWaferState state)
    {
        bool couldAssign = CanAssign;
        WaferId = waferId;
        WaferState = state;
        if (!CanAssign)
        {
            Sequence = string.Empty;
            return false;
        }

        return !couldAssign;
    }

    /// <summary>
    /// 账上的一片 → 状态列：物理状态不正常的先显示物理状态，正常片按工艺状态。
    /// </summary>
    public static JobWaferState StateOf(WaferDto wafer)
    {
        switch (wafer.Status)
        {
            case "Crossed":
                return JobWaferState.Crossed;
            case "Double":
                return JobWaferState.Double;
            case "Unknown":
                return JobWaferState.Unknown;
        }

        switch (wafer.ProcessState)
        {
            case "InProcess":
                return JobWaferState.InProcess;
            case "Completed":
                return JobWaferState.Completed;
            case "Failed":
                return JobWaferState.Failed;
            case "Aborted":
                return JobWaferState.Aborted;
            default:
                return JobWaferState.Idle;
        }
    }

    /// <summary>
    /// 账上没登记这个 LoadPort 时退回按 Mapping 结果：叠片、交叉片照实显示，其他有片的算待处理，空槽、认不出的算没片。
    /// </summary>
    public static JobWaferState StateOf(LoadPortSlotState state)
    {
        switch (state)
        {
            case LoadPortSlotState.DoubleSlotted:
                return JobWaferState.Double;
            case LoadPortSlotState.CrossSlotted:
                return JobWaferState.Crossed;
            case LoadPortSlotState.NotEmpty:
            case LoadPortSlotState.CorrectlyOccupied:
                return JobWaferState.Idle;
            default:
                return JobWaferState.None;
        }
    }
}
