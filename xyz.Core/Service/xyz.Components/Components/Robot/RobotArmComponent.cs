using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// Robot Arm
/// </summary>
[Component(description: "机械手指（Robot 的 Arm，本身是一根 Arm 轴）")]
public class RobotArmComponent : RobotAxisComponent, IRobotArm
{
    #region SC

    [SCEditor("1", "Arm", "手指号（取放与主动推送协议用，从 1 开始）", Required = true)]
    public int Number { get; set; } = 1;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "0", max: "60000",
        @default: "3000", description: "取放做完后等在位传感器跟上的上限（取完要有片、放完要没片；挂了 WaferSensor 才用）")]
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

    private readonly object _waferGate = new();

    /// <summary>0 = 还没收到推送，1 = 有片，2 = 无片。</summary>
    private int _hasWafer;

    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "手指上是否有片（挂了在位传感器按它，没挂按设备推送；null=还没读到）")]
    public bool? SensorHasWafer
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
    /// 账上这只手有没有片；账没开为 false
    /// </summary>
    public bool LedgerHasWafer
    {
        get
        {
            var ledger = ComponentBase.Find<IWaferManagerComponent>();
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
