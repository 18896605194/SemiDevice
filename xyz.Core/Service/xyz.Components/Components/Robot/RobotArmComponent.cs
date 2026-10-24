using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// Robot Arm
/// </summary>
[Component(description: "机械手指（Robot 的 Arm，本身是一根 Arm 轴）")]
public class RobotArmComponent : RobotAxisComponent, IRobotArm
{
    #region SC

    [Machine("1", "Arm", "手指号（取放与主动推送协议用，从 1 开始）", Required = true)]
    public int Number { get; set; } = 1;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "0", max: "60000",
        @default: "3000", description: "取放做完后等持片反馈跟上的上限（取完要有片、放完要没片；启用持片检查后使用）")]
    public int WaferSensorTimeoutMs
    {
        get { return GetEcInt(nameof(WaferSensorTimeoutMs)); }
        set { SetEcInt(nameof(WaferSensorTimeoutMs), value); }
    }

    #endregion

    /// <summary>
    /// 有片传感器
    /// </summary>
    public IDiSensor? WaferSensor { get; private set; }

    /// <summary>
    /// 默认配置了 DI 在位传感器就启用持片检查；其他反馈来源的手指组件可重写。
    /// 是否启用由配置决定，不随当前读数是否有效而改变。
    /// </summary>
    public virtual bool IsWaferCheckEnabled => WaferSensor is not null;

    private readonly object _waferGate = new();

    /// <summary>0 = 还没收到推送，1 = 有片，2 = 无片。</summary>
    private int _hasWafer;

    // 属性统一为 HasWafer，对外采集名保持兼容。
    [VariableMark(VariableType.SV, ValueFormat.Bool,
        description: "手指上是否有片（挂了在位传感器按它，没挂按设备推送；null=还没读到）", Name = "SensorHasWafer")]
    public virtual bool? HasWafer
    {
        get
        {
            var sensor = WaferSensor;
            if (sensor is not null)
            {
                return sensor.IsActive;
            }

            lock (_waferGate)
            {
                switch (_hasWafer)
                {
                    case 1:
                        return true;
                    case 2:
                        return false;
                    default:
                        return null;
                }
            }
        }
    }

    /// <summary>
    /// 这只手属于哪台机械手，机械手初始化时给
    /// </summary>
    private string _robot = string.Empty;

    /// <summary>
    /// 晶圆账（开机 Bind 时取）；没装是 null
    /// </summary>
    private ILedgerManager? _ledger;

    /// <summary>
    /// 账上这只手有没有片；账没开为 false
    /// </summary>
    public bool LedgerHasWafer
    {
        get
        {
            var ledger = _ledger;
            if (ledger is null || !ledger.IsEnable || _robot.Length == 0)
            {
                return false;
            }

            return ledger.Get(_robot, Number) is not null;
        }
    }

    /// <summary>
    /// 记下这只手属于哪台机械手
    /// </summary>
    public void SetRobot(string robot)
    {
        _robot = robot;
    }

    public override bool InitComponent()
    {
        WaferSensor = FindChild<IDiSensor>("WaferSensor");
        return base.InitComponent();
    }

    public override void Bind()
    {
        _ledger = IocHelper.GetService<ILedgerManager>();
    }

    /// <summary>
    /// 记下控制器推送的手指在位。驱动路由线程上调，只翻标志不做重活。
    /// </summary>
    public void UpdateWaferPresence(bool hasWafer)
    {
        lock (_waferGate)
        {
            _hasWafer = hasWafer ? 1 : 2;
        }
    }
}
