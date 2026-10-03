using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 一条摆臂的显示模型：摆到哪、在不在动，以及装在它上面的 Lift 和喷嘴。腔体三维图绑它摆臂、升降、出液。
/// 部件组成（路径、Lift、喷嘴）变了由 ChamberPartsModel 整条重建，这里只就地刷新状态。
/// </summary>
public class ChamberArmModel : ObservableObject
{
    public ChamberArmModel(ChamberArmDto dto)
    {
        Path = dto.Path;
        Nozzles = dto.Nozzles.Select(nozzle => new ChamberNozzleModel(nozzle)).ToList();
        foreach (var nozzle in Nozzles)
        {
            nozzle.PropertyChanged += OnNozzleChanged;
        }

        Update(dto);
    }

    /// <summary>组件全路径，如 "Chamber1.Arm1"。</summary>
    public string Path { get; }

    /// <summary>装在这条臂上的升降气缸；没配时 IsPresent 为 false。</summary>
    public ChamberCylinderModel Lift { get; } = new();

    /// <summary>装在这条臂上的喷嘴，按 sc.xml 里的先后。</summary>
    public IReadOnlyList<ChamberNozzleModel> Nozzles { get; }

    private double _reach;

    /// <summary>摆到哪：0 = Home，1 = 工艺位（Wafer 中心），后端按轴位置和标定换算好的。</summary>
    public double Reach
    {
        get => _reach;
        private set => SetProperty(ref _reach, value);
    }

    private double _edgeReach;

    /// <summary>第一个边缘在 Reach 上的位置（Edge / Center）；0 表示还没示教，三维图按 Home → 中心一段画。</summary>
    public double EdgeReach
    {
        get => _edgeReach;
        private set => SetProperty(ref _edgeReach, value);
    }

    private bool _isMoving;

    /// <summary>轴在动，三维图据此高亮。</summary>
    public bool IsMoving
    {
        get => _isMoving;
        private set => SetProperty(ref _isMoving, value);
    }

    /// <summary>有喷嘴在出液：在 Home 时接液杯据此亮起来。</summary>
    public bool IsAnyNozzleOn => Nozzles.Any(nozzle => nozzle.IsOn);

    /// <summary>用推送就地刷新（界面线程调用）；调用方保证部件组成跟建这条臂时一样。</summary>
    public void Update(ChamberArmDto dto)
    {
        Reach = dto.Reach;
        EdgeReach = dto.EdgeReach;
        IsMoving = dto.IsMoving;
        Lift.Update(dto.Lift);
        for (int i = 0; i < Nozzles.Count; i++)
        {
            Nozzles[i].Update(dto.Nozzles[i]);
        }
    }

    private void OnNozzleChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ChamberNozzleModel.IsOn))
        {
            OnPropertyChanged(nameof(IsAnyNozzleOn));
        }
    }
}
