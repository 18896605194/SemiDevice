using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 晶圆账上的片（WaferDto）→ 圆片控件的状态色（WaferModel.State）。手动页的机械手手指、LoadPort 花篮、腔体、调度图卡片都用这一份，
/// 颜色跟账单调整页的色条和图例一一对应（用户："颜色对应状态"）：交叉片、叠片、陪片、状态不明按物理状态上色，
/// 正常片按工艺状态——未处理（灰白，Load 完 Mapping 落账的片就是它）/ 工艺中 / 已完成 / 失败或中止。
/// </summary>
public static class WaferStates
{
    /// <summary>
    /// 传感器说有片、账上却没有：画成"在途"色，提示账实不符（该去 设置 → 账单调整 对账）。
    /// </summary>
    public const string Unledgered = "Transfer";

    /// <summary>未处理（账单调整页图例的"未处理"，灰白）。</summary>
    public const string Idle = "IdleNojob";

    public static string Of(WaferDto wafer)
    {
        switch (wafer.Status)
        {
            case "Crossed":
            case "Double":
            case "Dummy":
            case "Unknown":
                return wafer.Status;
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
                return Idle;
        }
    }
}
