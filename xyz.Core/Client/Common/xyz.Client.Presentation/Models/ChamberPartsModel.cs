using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 腔体部件的显示模型：腔体三维图和手动页的部件按钮都绑它，内容来自后端的部件状态推送（按 sc.xml 结构生成）。
/// 推送来了就地刷新；部件组成（sc 里配了哪些部件）变了才重建摆臂和按钮组，并把 Revision 加一，三维图据此重搭。
/// </summary>
public class ChamberPartsModel : ObservableObject
{
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

    /// <summary>部件操作区的按钮组：门、Bowl、旋转电机，再每条摆臂、它的 Lift、它的每路喷嘴各一组。</summary>
    public ObservableCollection<ChamberPartGroup> Groups { get; } = [];

    private int _revision;

    /// <summary>部件组成的版本：第一次收到推送、或者 sc 里配的部件变了就加一。</summary>
    public int Revision
    {
        get => _revision;
        private set => SetProperty(ref _revision, value);
    }

    /// <summary>
    /// 用推送就地刷新（界面线程调用）：部件组成没变只改状态，变了重建摆臂和按钮组。
    /// </summary>
    public void Update(ChamberPartsDto dto)
    {
        bool rebuild = Revision == 0 || !SameStructure(dto);
        _module = dto.Name;
        Door.Update(dto.Door);
        Bowl.Update(dto.Bowl);
        Spin.Update(dto.Spin);
        if (!rebuild)
        {
            for (int i = 0; i < Arms.Count; i++)
            {
                Arms[i].Update(dto.Arms[i]);
            }

            return;
        }

        Arms.Clear();
        foreach (var arm in dto.Arms)
        {
            Arms.Add(new ChamberArmModel(arm));
        }

        RebuildGroups();
        Revision++;
    }

    /// <summary>部件组成是否跟现在一样：看腔体名、各部件有没有、路径，摆臂的 Lift 和喷嘴。</summary>
    private bool SameStructure(ChamberPartsDto dto)
    {
        if (dto.Name != _module
            || !SamePart(Door, dto.Door)
            || !SamePart(Bowl, dto.Bowl)
            || Spin.IsPresent != (dto.Spin is not null)
            || Spin.Path != (dto.Spin?.Path ?? string.Empty)
            || Arms.Count != dto.Arms.Count)
        {
            return false;
        }

        for (int i = 0; i < Arms.Count; i++)
        {
            var arm = Arms[i];
            var next = dto.Arms[i];
            if (arm.Path != next.Path || !SamePart(arm.Lift, next.Lift) || arm.Nozzles.Count != next.Nozzles.Count)
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

    private static bool SamePart(ChamberCylinderModel model, ChamberCylinderDto? dto)
    {
        if (dto is null)
        {
            return !model.IsPresent;
        }

        return model.IsPresent && model.Path == dto.Path;
    }

    /// <summary>按现在的部件组成重建按钮组；按钮上的字按当前语言取（换语言要重启客户端）。</summary>
    private void RebuildGroups()
    {
        Groups.Clear();
        if (Door.IsPresent)
        {
            AddGroup(Door.Path,
                (ChamberPartAction.Open, "chambermanual.part.open"),
                (ChamberPartAction.Close, "chambermanual.part.close"));
        }

        if (Bowl.IsPresent)
        {
            AddGroup(Bowl.Path,
                (ChamberPartAction.Open, "chambermanual.part.up"),
                (ChamberPartAction.Close, "chambermanual.part.down"));
        }

        if (Spin.IsPresent)
        {
            AddGroup(Spin.Path,
                (ChamberPartAction.Start, "chambermanual.part.spin"),
                (ChamberPartAction.Stop, "chambermanual.part.stop"));
        }

        foreach (var arm in Arms)
        {
            AddGroup(arm.Path,
                (ChamberPartAction.Home, "action.home"),
                (ChamberPartAction.Center, "chambermanual.part.center"));
            if (arm.Lift.IsPresent)
            {
                AddGroup(arm.Lift.Path,
                    (ChamberPartAction.Open, "chambermanual.part.up"),
                    (ChamberPartAction.Close, "chambermanual.part.down"));
            }

            foreach (var nozzle in arm.Nozzles)
            {
                AddGroup(nozzle.Path,
                    (ChamberPartAction.On, "chambermanual.part.dispense"),
                    (ChamberPartAction.Off, "chambermanual.part.stop_dispense"));
            }
        }
    }

    private void AddGroup(string path, params (ChamberPartAction Action, string TextKey)[] actions)
    {
        string title = RelativePath(path);
        var items = actions
            .Select(item => new ChamberPartActionItem(title, path, item.Action, L10n.Get(item.TextKey)))
            .ToList();
        Groups.Add(new ChamberPartGroup(title, items));
    }

    /// <summary>部件路径去掉前面的腔体名："Chamber1.Arm1.Lift" → "Arm1.Lift"，照 sc.xml 原样显示。</summary>
    private string RelativePath(string path)
    {
        string prefix = _module + ".";
        if (!string.IsNullOrEmpty(_module) && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return path[prefix.Length..];
        }

        return path;
    }
}
