using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

namespace xyz.Client.Manual.Models;

/// <summary>
/// 腔体手动页气缸表的一行：名字照 sc 路径（去掉腔体名），状态升到位 / 降到位 / 未知（命令发了、到位信号还没亮）来自部件推送。
/// 升 = 气缸开侧（门开、Bowl 升、Lift 升），降 = 关侧；状态文字在页面上按 Position 取语言包。
/// </summary>
public class CylinderPartModel : ObservableObject
{
    /// <param name="module">腔体模块名，行名去掉它（"Chamber1.Arm1.Lift" → "Arm1.Lift"）。</param>
    /// <param name="dto">这个气缸的推送。</param>
    public CylinderPartModel(string module, PartDto dto)
    {
        Path = dto.Path;
        string prefix = module + ".";
        Name = Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? Path[prefix.Length..] : Path;
        Update(dto);
    }

    /// <summary>组件全路径，如 "Chamber1.Bowl1"；动作按它找部件。</summary>
    public string Path { get; }

    /// <summary>行名：sc 路径去掉腔体名，照 sc 原样显示。</summary>
    public string Name { get; }

    private TwoStatePosition _position;

    /// <summary>在哪一侧：升到位（开侧）、降到位（关侧）、未知。</summary>
    public TwoStatePosition Position
    {
        get => _position;
        private set => SetProperty(ref _position, value);
    }

    /// <summary>用推送就地刷新（界面线程调用）。</summary>
    public void Update(PartDto dto)
    {
        Position = ChamberCylinderModel.ParsePosition(dto.Get(PartValueNames.Position));
    }
}
