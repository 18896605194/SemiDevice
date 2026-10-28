using xyz.Shared.Dtos;

namespace xyz._35021.Shared.Dtos;

/// <summary>
/// IPA 推给界面的那一份：IPA 自己的状态，加上它下面各槽（液位、状态、信号）；token 是模块名，留存
/// </summary>
public class IpaDto
{
    /// <summary>
    /// 模块名（如 IPA），跟 推送 token 一样
    /// </summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>
    /// 状态码
    /// </summary>
    public int State { get; set; }

    /// <summary>
    /// 下面的槽，按 machine.xml 的先后
    /// </summary>
    public List<TankDto> Tanks { get; set; } = [];

    /// <summary>
    /// 跟上一次推的比有没有变化；没有上一次算变化
    /// </summary>
    public bool HasStateChanged(IpaDto? previous)
    {
        if (previous is null)
        {
            return true;
        }

        return Module != previous.Module || State != previous.State || !Tanks.SequenceEqual(previous.Tanks);
    }
}
