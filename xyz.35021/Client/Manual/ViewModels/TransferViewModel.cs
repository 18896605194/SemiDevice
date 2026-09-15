using System.Collections.ObjectModel;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;
using xyz._35021.Client.Manual.Models;

namespace xyz._35021.Client.Manual.ViewModels;

/// <summary>
/// 35021 Transfer 调度界面 ViewModel，目前先接机械手：
/// 订阅 RobotDto 刷新状态环与手臂上的片；Robot 控件只绑目标姿态（Rotation、各手臂 Extension），
/// 运动顺序（收回 → 转向 → 伸出）由控件自己排。
/// </summary>
public class TransferViewModel : BaseViewModel, IDisposable
{
    #region 布局

    /// <summary>
    /// 搬运地图的设计尺寸，外层 Viewbox 等比缩放。
    /// </summary>
    public const double MapWidth = 960;

    public const double MapHeight = 720;

    /// <summary>
    /// 机械手控件尺寸，放在地图中心。
    /// </summary>
    public const double RobotSize = 400;

    public const double StationWidth = 190;

    public const double StationHeight = 92;

    /// <summary>
    /// 站点卡片中心到机械手中心的距离。
    /// </summary>
    private const double StationDistance = 240;

    public double RobotLeft => (MapWidth - RobotSize) / 2;

    public double RobotTop => (MapHeight - RobotSize) / 2;

    #endregion

    #region Column

    private const string DefaultModuleName = "Robot1";

    private string _moduleName = DefaultModuleName;

    /// <summary>
    /// 机械手模块名，与 EventBus token / gRPC 参数一致，如 "Robot1"。
    /// </summary>
    public string ModuleName
    {
        get => _moduleName;
        private set => SetProperty(ref _moduleName, value);
    }

    /// <summary>
    /// 站点：名称与机械手站点表一致；卡片按转台角度围着机械手摆，机械手对准哪个站点就朝哪个卡片。
    /// </summary>
    public ObservableCollection<TransferStationModel> Stations { get; } = new();

    /// <summary>
    /// 机械手各手臂：手臂上的片来自状态推送；伸出量（Extension）由调度动作设置。
    /// </summary>
    public ObservableCollection<RobotArmModel> Arms { get; } = new();

    private int _armCount = 2;

    /// <summary>
    /// 手指数量：按设备推送的手臂在位信息取最大手臂号（1~4）。
    /// </summary>
    public int ArmCount
    {
        get => _armCount;
        private set => SetProperty(ref _armCount, value);
    }

    private double _rotation;

    /// <summary>
    /// 转台目标角度（度，0 = 正上方，顺时针）：调度动作设为目标站点的 Angle。
    /// </summary>
    public double Rotation
    {
        get => _rotation;
        set => SetProperty(ref _rotation, value);
    }

    private RobotDisplayStatus _robotStatus = RobotDisplayStatus.Offline;

    public RobotDisplayStatus RobotStatus
    {
        get => _robotStatus;
        private set => SetProperty(ref _robotStatus, value);
    }

    #endregion

    #region Service

    private readonly IRobotService _service;

    private IDisposable? _stateSubscription;

    #endregion

    public TransferViewModel()
    {
        _service = GrpcClientFactory.Create<IRobotService>();

        // 35021 的站点与 sc.xml 里 Robot1 的 Stations 一致；角度决定卡片摆放位置与机械手朝向。
        AddStation("LoadPort1", 218);
        AddStation("LoadPort2", 142);
        AddStation("Chamber1", 322);
        AddStation("Chamber2", 38);
    }

    public override void Init()
    {
        try
        {
            // module 传空串 = 返回全部 Robot；只有一个时返回单对象，多个返回数组。界面先接第一台。
            var response = _service.GetStateAsync(string.Empty).GetAwaiter().GetResult();
            response.EnsureSuccess();

            var json = response.Data?.TrimStart() ?? string.Empty;
            var robot = json.StartsWith('[')
                ? JsonHelper.Deserialize<List<RobotDto>>(json)?.FirstOrDefault()
                : JsonHelper.Deserialize<RobotDto>(json);
            if (robot is not null && !string.IsNullOrEmpty(robot.Name))
            {
                ModuleName = robot.Name;
                OnStateReceived(robot);
            }
        }
        catch (Exception exception)
        {
            // 读不到也不能让页面崩：记日志，状态推送到了会自动刷新。
            ClientLog.Error(nameof(TransferViewModel), $"读取机械手状态失败：{exception.Message}");
        }

        _stateSubscription?.Dispose();
        _stateSubscription = EventBus.Register<RobotDto>(ModuleName, OnStateReceived);
    }

    public void Dispose()
    {
        _stateSubscription?.Dispose();
        _stateSubscription = null;
    }

    private void OnStateReceived(RobotDto dto)
    {
        RobotStatus = ToDisplayStatus(dto);

        if (dto.Arms.Count > 0)
        {
            ArmCount = Math.Clamp(dto.Arms.Max(arm => arm.Arm), 1, 4);
        }

        foreach (var reported in dto.Arms)
        {
            var arm = GetArm(reported.Arm);
            if (reported.HasWafer)
            {
                // 设备只报有无片，槽位号未知：显示一片"搬运中"的片。
                arm.Wafer ??= new WaferModel { State = "Transfer" };
            }
            else
            {
                arm.Wafer = null;
            }
        }
    }

    /// <summary>
    /// 底座状态环：未连接灰、报错红、未初始化黄、空闲绿、其余（动作中/中止中）蓝色流光。
    /// 状态码对应 xyz.Modules 的 ModuleState/RobotState。
    /// </summary>
    private static RobotDisplayStatus ToDisplayStatus(RobotDto dto)
    {
        if (!dto.IsConnected)
        {
            return RobotDisplayStatus.Offline;
        }

        if (dto.State == 40 || !string.IsNullOrEmpty(dto.DeviceError))
        {
            return RobotDisplayStatus.Alarm;
        }

        return dto.State switch
        {
            10 or 20 => RobotDisplayStatus.NotReady,
            30 => RobotDisplayStatus.Idle,
            _ => RobotDisplayStatus.Busy,
        };
    }

    private void AddStation(string name, double angle)
    {
        double radians = angle * Math.PI / 180;
        double centerX = MapWidth / 2 + Math.Sin(radians) * StationDistance;
        double centerY = MapHeight / 2 - Math.Cos(radians) * StationDistance;
        Stations.Add(new TransferStationModel(name, angle, centerX - StationWidth / 2, centerY - StationHeight / 2));
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
