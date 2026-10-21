using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;

namespace xyz.Client.DataCenter.Models;

/// <summary>
/// Job 历史页任务表的一行：一片的一个任务（取片、放片、工艺……）
/// </summary>
public class JobTaskHistoryModel
{
    public JobTaskHistoryModel(JobTaskDto dto)
    {
        Kind = dto.Kind;
        State = dto.State;
        Station = StationOf(dto);
        Recipe = dto.Recipe;
        if (!string.IsNullOrEmpty(dto.Recipe) && dto.RecipeRevision > 0)
        {
            Recipe = L10n.Get("jobhistory.revision", dto.Recipe, dto.RecipeRevision);
        }

        Slot = string.Empty;
        if (dto.Slot > 0)
        {
            Slot = dto.Slot.ToString();
        }

        Robot = string.Empty;
        if (!string.IsNullOrEmpty(dto.Robot))
        {
            Robot = L10n.Get("jobhistory.robot_arm", dto.Robot, dto.Arm);
        }

        Reason = string.Empty;
        if (!string.IsNullOrEmpty(dto.Code))
        {
            Reason = L10n.Get(dto.Code, dto.Args);
        }
    }

    /// <summary>
    /// Pick / Place / Process，站点自己声明的站内任务是它起的名字（界面原样显示）
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Waiting / Running / Done / Error / Cancelled
    /// </summary>
    public string State { get; }

    /// <summary>
    /// 实际做的站点；还没派过的写候选的站点组
    /// </summary>
    public string Station { get; }

    public string Slot { get; }

    /// <summary>
    /// 工艺配方名带版本，不是工艺的为空
    /// </summary>
    public string Recipe { get; }

    /// <summary>
    /// 取放用的机械手和手，站内任务为空
    /// </summary>
    public string Robot { get; }

    /// <summary>
    /// 出错的原因，没出错为空
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// 派过的写实际站点，没派过（没轮到就结束了）写候选的站点组
    /// </summary>
    private static string StationOf(JobTaskDto dto)
    {
        if (!string.IsNullOrEmpty(dto.Station))
        {
            return dto.Station;
        }

        return string.Join(" / ", dto.Stations);
    }
}
