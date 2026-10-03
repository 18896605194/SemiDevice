using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 晶圆账上的片（WaferDto）→ 圆片控件的状态色（WaferModel.State）。手动页的机械手手指、LoadPort 花篮、调度图卡片都用这一份，
/// 跟账单调整页的色条一个意思：交叉片、叠片按物理状态上色，正常片按工艺状态（未做 / 工艺中 / 做完 / 失败或中止）。
/// </summary>
public static class WaferStates
{
    /// <summary>
    /// 传感器说有片、账上却没有：画成"在途"色，提示账实不符（该去 设置 → 账单调整 对账）。
    /// </summary>
    public const string Unledgered = "Transfer";

    public static string Of(WaferDto wafer)
    {
        switch (wafer.Status)
        {
            case "Crossed":
                return "Crossed";
            case "Double":
                return "Double";
        }

        switch (wafer.ProcessState)
        {
            case "InProcess":
                return "Process";
            case "Completed":
                return "Completed";
            case "Failed":
            case "Aborted":
                return "Error";
            default:
                return "IdleHasjob";
        }
    }
}
