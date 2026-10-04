using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 腔体三维图的部件显示模型：内容来自后端的部件推送（ModulePartsDto，按 sc.xml 结构生成的通用部件 + 实时数据）。
/// 推送里不分门、Bowl、摆臂，这里按 sc 的结构认出三维图要画的：腔体下名叫 Door 的气缸是门、名字以 Bowl 开头的第一个气缸是 Bowl、
/// 第一个旋转电机是卡盘、每条摆臂（它下面第一个气缸是 Lift，下面的阀按 sc 里的先后是喷嘴）。sc 里没配的就不画。
/// 推送来了就地刷新；部件组成变了才重建摆臂，并把 Revision 加一，三维图据此重搭。
/// </summary>
public class ChamberPartsModel : ObservableObject
{
    /// <summary>sc.xml 里腔门的节点名：门和 Bowl 都是气缸，只能按名字认。</summary>
    private const string DoorName = "Door";

    /// <summary>sc.xml 里 Bowl 节点名的开头：现在只配一层叫 Bowl1；以后加层（Bowl2……）三维图也只画第一个。</summary>
    private const string BowlPrefix = "Bowl";

    private string _module = string.Empty;

    /// <summary>腔体模块名，如 "Chamber1"；还没收到推送时为空。</summary>
    public string Module => _module;

    /// <summary>腔门；sc 里没配时 IsPresent 为 false。</summary>
    public ChamberCylinderModel Door { get; } = new();

    /// <summary>Bowl；sc 里没配时 IsPresent 为 false。</summary>
    public ChamberCylinderModel Bowl { get; } = new();

    /// <summary>旋转电机；sc 里没配时 IsPresent 为 false。</summary>
    public ChamberSpinModel Spin { get; } = new();

    /// <summary>各条摆臂，按 sc.xml 里的先后。</summary>
    public ObservableCollection<ChamberArmModel> Arms { get; } = [];

    private int _revision;

    /// <summary>部件组成的版本：第一次收到推送、或者 sc 里配的部件变了就加一。</summary>
    public int Revision
    {
        get => _revision;
        private set => SetProperty(ref _revision, value);
    }

    /// <summary>
    /// 用推送就地刷新（界面线程调用）：部件组成没变只改状态，变了重建摆臂。
    /// </summary>
    public void Update(ModulePartsDto dto)
    {
        var layout = Layout.Of(dto);
        bool rebuild = Revision == 0 || !SameStructure(dto.Module, layout);
        _module = dto.Module;
        Door.Update(layout.Door);
        Bowl.Update(layout.Bowl);
        Spin.Update(layout.Spin);
        if (!rebuild)
        {
            for (int i = 0; i < Arms.Count; i++)
            {
                var arm = layout.Arms[i];
                Arms[i].Update(arm.Arm, arm.Lift, arm.Nozzles);
            }

            return;
        }

        Arms.Clear();
        foreach (var arm in layout.Arms)
        {
            Arms.Add(new ChamberArmModel(arm.Arm, arm.Lift, arm.Nozzles));
        }

        Revision++;
    }

    /// <summary>部件组成是否跟现在一样：看腔体名、各部件有没有、路径，摆臂的 Lift 和喷嘴。</summary>
    private bool SameStructure(string module, Layout layout)
    {
        if (module != _module
            || !SamePart(Door, layout.Door)
            || !SamePart(Bowl, layout.Bowl)
            || Spin.IsPresent != (layout.Spin is not null)
            || Spin.Path != (layout.Spin?.Path ?? string.Empty)
            || Arms.Count != layout.Arms.Count)
        {
            return false;
        }

        for (int i = 0; i < Arms.Count; i++)
        {
            var arm = Arms[i];
            var next = layout.Arms[i];
            if (arm.Path != next.Arm.Path || !SamePart(arm.Lift, next.Lift) || arm.Nozzles.Count != next.Nozzles.Count)
            {
                return false;
            }

            for (int j = 0; j < arm.Nozzles.Count; j++)
            {
                if (arm.Nozzles[j].Path != next.Nozzles[j].Path)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool SamePart(ChamberCylinderModel model, PartDto? dto)
    {
        if (dto is null)
        {
            return !model.IsPresent;
        }

        return model.IsPresent && model.Path == dto.Path;
    }

    /// <summary>一条摆臂和装在它上面的 Lift、喷嘴。</summary>
    private sealed record ArmLayout(PartDto Arm, PartDto? Lift, IReadOnlyList<PartDto> Nozzles);

    /// <summary>从推送里认出来的三维图部件。</summary>
    private sealed record Layout(PartDto? Door, PartDto? Bowl, PartDto? Spin, IReadOnlyList<ArmLayout> Arms)
    {
        public static Layout Of(ModulePartsDto dto)
        {
            PartDto? door = null;
            PartDto? bowl = null;
            PartDto? spin = null;
            var arms = new List<ArmLayout>();
            foreach (var part in dto.Parts)
            {
                if (part.Kind == PartKinds.TwoState && IsDirectChild(dto.Module, part.Path))
                {
                    string name = part.Path[(dto.Module.Length + 1)..];
                    if (door is null && string.Equals(name, DoorName, StringComparison.OrdinalIgnoreCase))
                    {
                        door = part;
                    }
                    else if (bowl is null && name.StartsWith(BowlPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        bowl = part;
                    }
                }
                else if (part.Type == PartKinds.SpinMotorType)
                {
                    spin ??= part;
                }
                else if (part.Type == PartKinds.ArmAxisType)
                {
                    string prefix = part.Path + ".";
                    var below = dto.Parts.Where(item => item.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                    arms.Add(new ArmLayout(part,
                        below.FirstOrDefault(item => item.Kind == PartKinds.TwoState),
                        below.Where(item => item.Kind == PartKinds.OneState).ToList()));
                }
            }

            return new Layout(door, bowl, spin, arms);
        }

        /// <summary>是不是腔体的直接子节点（"Chamber1.Door" 是，"Chamber1.Arm1.Lift" 不是）。</summary>
        private static bool IsDirectChild(string module, string path)
        {
            string prefix = module + ".";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path.IndexOf('.', prefix.Length) < 0;
        }
    }
}
