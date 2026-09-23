using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

namespace xyz.Client.Manual.Models;

/// <summary>
/// 机械手显示模型，界面上的 Robot 控件直接绑它；只做显示，站点表 / 去哪 / 朝哪（Stations / Travel / Rotation）、轴坐标都由后端按 sc.xml 配置推过来。
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

    private int _state;

    /// <summary>模块状态码，取值见 ModuleState/RobotState。</summary>
    public int State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateText));
            }
        }
    }

    private ModuleMode _mode;

    /// <summary>模块模式（Online=参与自动调度 / Offline）。</summary>
    public ModuleMode Mode
    {
        get => _mode;
        private set => SetProperty(ref _mode, value);
    }

    private bool _isConnected;

    /// <summary>驱动连接是否可用。</summary>
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(HasDeviceError));
            }
        }
    }

    private bool? _isServoOn;

    /// <summary>查询反馈：伺服是否上使能；null 表示反馈不可用。</summary>
    public bool? IsServoOn
    {
        get => _isServoOn;
        private set
        {
            if (SetProperty(ref _isServoOn, value))
            {
                OnPropertyChanged(nameof(ServoText));
            }
        }
    }

    private string? _deviceError;

    /// <summary>查询反馈：设备当前报错；null 表示无报错或反馈不可用。</summary>
    public string? DeviceError
    {
        get => _deviceError;
        private set
        {
            if (SetProperty(ref _deviceError, value))
            {
                OnPropertyChanged(nameof(DeviceErrorText));
                OnPropertyChanged(nameof(HasDeviceError));
            }
        }
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
        private set
        {
            if (SetProperty(ref _rotation, value))
            {
                OnPropertyChanged(nameof(RotationText));
            }
        }
    }

    private double _travel;

    /// <summary>水平平移距离：当前站点在 sc.xml 里配置的 Travel。</summary>
    public double Travel
    {
        get => _travel;
        private set => SetProperty(ref _travel, value);
    }

    private List<string> _stations = [];

    /// <summary>站点表里的全部站点名（后端 sc.xml 配置），界面下拉用。</summary>
    public List<string> Stations
    {
        get => _stations;
        private set => SetProperty(ref _stations, value);
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

    private List<RobotAxisModel> _axisPositions = [];

    /// <summary>各轴当前坐标（扫描查询刷新），按轴表顺序；还没查到的轴不在列表里。</summary>
    public List<RobotAxisModel> AxisPositions
    {
        get => _axisPositions;
        private set => SetProperty(ref _axisPositions, value);
    }

    /// <summary>
    /// 状态文字（按当前语言）。码值对应 xyz.Modules 的 ModuleState/RobotState，未收录的码显示原值。
    /// </summary>
    public string StateText
    {
        get
        {
            switch (State)
            {
                case 10: return L10n.Get("module.state.not_init");
                case 20: return L10n.Get("module.state.initing");
                case 30: return L10n.Get("module.state.idle");
                case 35: return L10n.Get("module.state.aborting");
                case 40: return L10n.Get("module.state.error");
                case 50: return L10n.Get("module.state.pre_transfer");
                case 60: return L10n.Get("module.state.transfer_ready");
                case 70: return L10n.Get("module.state.transferring");
                case 80: return L10n.Get("module.state.transfer_complete");
                case 130: return L10n.Get("module.state.homing");
                case 200: return L10n.Get("module.state.homing");
                case 210: return L10n.Get("module.state.picking");
                case 220: return L10n.Get("module.state.placing");
                default: return L10n.Get("module.state.unknown", State);
            }
        }
    }

    /// <summary>转台方位文字（按当前语言）。</summary>
    public string RotationText
    {
        get
        {
            switch (Rotation)
            {
                case RobotDirection.North: return L10n.Get("robot.direction.north");
                case RobotDirection.East: return L10n.Get("robot.direction.east");
                case RobotDirection.South: return L10n.Get("robot.direction.south");
                case RobotDirection.West: return L10n.Get("robot.direction.west");
                default: return Rotation.ToString();
            }
        }
    }

    /// <summary>伺服状态文字：ON / OFF；反馈不可用显示占位符。</summary>
    public string ServoText
    {
        get
        {
            if (IsServoOn == true)
            {
                return L10n.Get("robotmanual.servo_on");
            }

            if (IsServoOn == false)
            {
                return L10n.Get("robotmanual.servo_off");
            }

            return L10n.Get("robotmanual.na");
        }
    }

    /// <summary>设备报错灯：已连接且报错有内容才亮。</summary>
    public bool HasDeviceError
    {
        get
        {
            if (!IsConnected)
            {
                return false;
            }

            return !string.IsNullOrEmpty(DeviceError);
        }
    }

    /// <summary>设备报错文字；无报错或反馈不可用显示占位符。</summary>
    public string DeviceErrorText
    {
        get
        {
            if (string.IsNullOrEmpty(DeviceError))
            {
                return L10n.Get("robotmanual.na");
            }

            return DeviceError;
        }
    }

    /// <summary>
    /// 用推送的状态就地刷新（界面线程调用）。
    /// </summary>
    public void Update(RobotDto dto)
    {
        Name = dto.Name;
        State = dto.State;
        Mode = dto.Mode;
        IsConnected = dto.IsConnected;
        IsServoOn = dto.IsServoOn;
        DeviceError = dto.DeviceError;
        Station = dto.Station;
        Rotation = dto.Rotation;
        Travel = dto.Travel;
        Stations = [.. dto.Stations];
        AxisPositions = dto.AxisPositions
            .Select(axis => new RobotAxisModel { Name = axis.Name, Position = axis.Position })
            .ToList();

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
