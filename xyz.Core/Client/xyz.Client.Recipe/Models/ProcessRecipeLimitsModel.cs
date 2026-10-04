using xyz.Shared.Dtos;

namespace xyz.Client.Recipe.Models;

/// <summary>
/// 工艺步骤各项的范围（来自后端：sc.xml 的 ProcessRecipe 节点、晶圆坐标、腔体工艺超时）。
/// 每一行步骤都拿着它：输入框的上下限直接绑这里，检查也按这里查，界面不写死数。
/// </summary>
public sealed class ProcessRecipeLimitsModel
{
    public ProcessRecipeLimitsModel(ProcessRecipeOptionsDto options)
    {
        MinSeconds = options.MinSeconds;
        MaxSeconds = options.MaxSeconds;
        MaxRpm = options.MaxRpm;
        MinFlow = options.MinFlow;
        MaxFlow = options.MaxFlow;
        MinPosition = options.MinPosition;
        MaxPosition = options.MaxPosition;
        MinScanSpeed = options.MinScanSpeed;
        MaxScanSpeed = options.MaxScanSpeed;
        MaxTotalSeconds = options.MaxTotalSeconds;
    }

    public double MinSeconds { get; }

    public double MaxSeconds { get; }

    /// <summary>
    /// 转速下限：0（停着泡）。
    /// </summary>
    public double MinRpm => 0;

    public double MaxRpm { get; }

    public double MinFlow { get; }

    public double MaxFlow { get; }

    /// <summary>
    /// 晶圆边缘（0）。
    /// </summary>
    public double MinPosition { get; }

    /// <summary>
    /// 晶圆中心（150）。
    /// </summary>
    public double MaxPosition { get; }

    public double MinScanSpeed { get; }

    public double MaxScanSpeed { get; }

    /// <summary>
    /// 合计时长上限（腔体工艺超时）；0 = 不限。
    /// </summary>
    public double MaxTotalSeconds { get; }
}
