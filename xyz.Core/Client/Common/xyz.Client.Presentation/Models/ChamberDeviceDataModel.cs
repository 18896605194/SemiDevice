using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 腔体三维图的设备显示模型：内容来自后端的设备状态推送（ChamberDeviceDataDto，结构跟 sc.xml 一样）。
/// 门、Bowl（画第一个）、卡盘、各条摆臂（带它的 Lift 和喷嘴）直接照推送的树取；sc 里没配的就不画。
/// 推送来了就地刷新；设备组成变了才重建摆臂，并把 Revision 加一，三维图据此重搭。
/// </summary>
public class ChamberDeviceDataModel : ObservableObject
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

    private int _revision;

    /// <summary>设备组成的版本：第一次收到推送、或者 sc 里配的设备变了就加一。</summary>
    public int Revision
    {
        get => _revision;
        private set => SetProperty(ref _revision, value);
    }

    /// <summary>
    /// 用推送就地刷新（界面线程调用）：设备组成没变只改状态，变了重建摆臂。
    /// </summary>
    public void Update(ChamberDeviceDataDto dto)
    {
        var bowl = dto.Bowls.FirstOrDefault();
        bool rebuild = Revision == 0 || !SameStructure(dto, bowl);
        _module = dto.Module;
        Door.Update(dto.Door);
        Bowl.Update(bowl);
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

        Revision++;
    }

    /// <summary>设备组成是否跟现在一样：看腔体名、各设备有没有、路径，摆臂的 Lift 和喷嘴。</summary>
    private bool SameStructure(ChamberDeviceDataDto dto, ChamberCylinderDto? bowl)
    {
        if (dto.Module != _module
            || !SameCylinder(Door, dto.Door)
            || !SameCylinder(Bowl, bowl)
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
            if (arm.Path != next.Path || !SameCylinder(arm.Lift, next.Lift) || arm.Nozzles.Count != next.Nozzles.Count)
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

    private static bool SameCylinder(ChamberCylinderModel model, ChamberCylinderDto? dto)
    {
        if (dto is null)
        {
            return !model.IsPresent;
        }

        return model.IsPresent && model.Path == dto.Path;
    }
}
