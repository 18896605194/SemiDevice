using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

namespace xyz.Client.Manual.Models;

/// <summary>
/// 机械手显示模型，界面上的 Robot 控件直接绑它；只做显示，去哪、朝哪（Travel / Rotation）由后端按 sc.xml 站点表推过来。
/// 状态推送来了就地刷新：手臂按手指号保留同一实例，控件的运动动画不会因为换实例被打断。
/// </summary>
public class RobotModel : ObservableObject
{
    private const int MaxArmCount = 4;

    private string _name = string.Empty;

    /// <summary>模块实例名，与 EventBus token 一致，如 "Robot1"。</summary>
    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    private string? _station;

    /// <summary>当前站点（最近一次取放片的站点名，如 LoadPort1）；还没取放过为 null。</summary>
    public string? Station
    {
        get => _station;
        private set => SetProperty(ref _station, value);
    }

    private RobotDirection _rotation;

    /// <summary>转台方位：当前站点在 sc.xml 里配置的 Rotation。</summary>
    public RobotDirection Rotation
    {
        get => _rotation;
        private set => SetProperty(ref _rotation, value);
    }

    private double _travel;

    /// <summary>水平平移距离：当前站点在 sc.xml 里配置的 Travel。</summary>
    public double Travel
    {
        get => _travel;
        private set => SetProperty(ref _travel, value);
    }

    private int _armCount = 2;

    /// <summary>手指数量：按推送的手指在位信息取最大手指号（1~4）。</summary>
    public int ArmCount
    {
        get => _armCount;
        private set => SetProperty(ref _armCount, value);
    }

    /// <summary>
    /// 各手臂：手臂上的片来自手指在位推送；设备只报有无片、槽位未知，显示一片"搬运中"的片。
    /// </summary>
    public ObservableCollection<RobotArmModel> Arms { get; } = new();

    /// <summary>
    /// 用推送的状态就地刷新（界面线程调用）。
    /// </summary>
    public void Update(RobotDto dto)
    {
        Name = dto.Name;
        Station = dto.Station;
        Rotation = dto.Rotation;
        Travel = dto.Travel;

        if (dto.Arms.Count > 0)
        {
            ArmCount = Math.Clamp(dto.Arms.Max(arm => arm.Arm), 1, MaxArmCount);
        }

        foreach (var reported in dto.Arms)
        {
            var arm = GetArm(reported.Arm);
            if (reported.HasWafer)
            {
                arm.Wafer ??= new WaferModel { State = "Transfer" };
            }
            else
            {
                arm.Wafer = null;
            }
        }
    }

    private RobotArmModel GetArm(int number)
    {
        var arm = Arms.FirstOrDefault(candidate => candidate.Arm == number);
        if (arm is null)
        {
            arm = new RobotArmModel { Arm = number };
            Arms.Add(arm);
        }

        return arm;
    }
}
