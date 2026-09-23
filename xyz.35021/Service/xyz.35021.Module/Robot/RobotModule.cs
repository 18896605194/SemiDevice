using System.Diagnostics;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Drivers.Communication;
using xyz.Drivers.Robot;
using xyz.Drivers.Robot.Reje;
using xyz.Drivers.Robot.Reje.Commands;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz._35021.Module.Robot.Operation;

namespace xyz._35021.Module.Robot;

/// <summary>
/// 35021 机台 Robot 模块：锐洁驱动 + 各动作操作（操作类在 Operation 文件夹）。
/// </summary>
[Component(description: "35021 Robot 模块")]
public class RobotModule : BaseRobotModule, IRobot
{
    #region 驱动连接

    protected override IRobotDriver CreateDriver()
    {
        var transport = CommunicationFactory.CreateTcp(Host, NetPort);
        return new RejeRobotDriver(new FrameCommunication(transport, new RejeFrameCodec()));
    }

    protected override void OnDriverCreated(IRobotDriver driver)
    {
        driver.OnSpontaneousEvent += OnDeviceEvent;
    }

    /// <summary>
    /// 设备主动推送：在驱动路由线程回调，只做轻量状态翻转。
    /// </summary>
    private void OnDeviceEvent(RobotDeviceEvent evt)
    {
        switch (evt.Kind)
        {
            case RobotDeviceEventKind.WaferPresence:
                NoteWaferPresence(evt.Arm, evt.HasWafer);
                break;

            case RobotDeviceEventKind.DeviceError:
                DeviceError = evt.Content;
                break;
        }
    }

    #endregion

    #region 扫描

    /// <summary>
    /// 手指在位推送未订阅成功时，每隔多少次查询重试一次订阅（首次查询即订阅）。
    /// </summary>
    private const int SubscribeRetryInterval = 20;

    private ActionStep _queryStep = ActionStep.SendCommand;
    private RobotCommand? _queryCommand;
    private int _queryCount;
    private bool _waferEventSubscribed;
    private readonly Stopwatch _queryWatch = new();

    protected override void OnScan()
    {
        base.OnScan();
        ScanDeviceStatus();
        PublishState();
    }

    /// <summary>
    /// 分周期下发查询并读取结果，不阻塞扫描线程：先订阅手指在位推送，之后轮流查设备报错与伺服使能。
    /// </summary>
    private void ScanDeviceStatus()
    {
        if (!IsEnable)
        {
            return;
        }

        var driver = Driver;
        if (driver is null)
        {
            return;
        }

        switch (_queryStep)
        {
            case ActionStep.SendCommand:
                if (!driver.IsConnected)
                {
                    break;
                }

                _queryCommand = CreateQueryCommand(driver);
                if (_queryCommand.Execute())
                {
                    _queryWatch.Restart();
                    _queryStep = ActionStep.WaitCommand;
                }

                break;

            case ActionStep.WaitCommand:
                if (!_queryCommand!.IsCompleted)
                {
                    var timeout = QueryDataTimeOut;
                    if (_queryWatch.ElapsedMilliseconds >= timeout)
                    {
                        // 驱动保留旧查询的在途项，旧回复到达前不会受理同名查询。
                        LogHelper.Warn(Name, $"{_queryCommand.Key} 查询超时（{timeout}ms）");
                        _queryCommand = null;
                        _queryWatch.Reset();
                        _queryStep = ActionStep.SendCommand;
                    }

                    break;
                }

                ApplyQueryResponse(_queryCommand);
                _queryCommand = null;
                _queryWatch.Reset();
                _queryStep = ActionStep.SendCommand;
                break;
        }
    }

    private RobotCommand CreateQueryCommand(IRobotDriver driver)
    {
        int count = _queryCount++;
        if (!_waferEventSubscribed && count % SubscribeRetryInterval == 0)
        {
            return new RejeSubscribeWaferEventCommand(driver);
        }

        return count % 2 == 0
            ? new RejeQueryErrorCommand(driver)
            : new RejeQueryEnableCommand(driver);
    }

    /// <summary>
    /// 只读查询的 Response 刷新模块状态。
    /// </summary>
    private void ApplyQueryResponse(RobotCommand command)
    {
        var response = command.Response!;
        switch (command)
        {
            case RejeSubscribeWaferEventCommand:
                _waferEventSubscribed = response.IsSuccess;
                if (!response.IsSuccess)
                {
                    LogHelper.Warn(Name, $"订阅手指在位推送失败：{response.Error}");
                }

                break;

            case RejeQueryEnableCommand when response.IsSuccess:
                IsServoOn = response.ServoOn;
                break;

            case RejeQueryErrorCommand when response.IsSuccess:
                DeviceError = response.DeviceError;
                break;
        }
    }

    #endregion

    #region Action

    public override ModuleOperation? Home()
    {
        return Begin(RobotAction.Home, new HomeOperation(this));
    }

    protected override ModuleOperation? ResetDevice()
    {
        return Begin(RobotAction.Reset, new ResetOperation(this));
    }

    protected override ModuleOperation? AbortDevice()
    {
        return Begin(RobotAction.Abort, new AbortOperation(this));
    }

    protected override ModuleOperation CreatePickOperation(int arm, int stationNumber, int slot)
    {
        return new PickOperation(this, arm, stationNumber, slot);
    }

    protected override ModuleOperation CreatePlaceOperation(int arm, int stationNumber, int slot)
    {
        return new PlaceOperation(this, arm, stationNumber, slot);
    }

    public override ModuleOperation? PowerOn()
    {
        return Begin(RobotAction.PowerOn, new PowerOnOperation(this));
    }

    public override ModuleOperation? PowerOff()
    {
        return Begin(RobotAction.PowerOff, new PowerOffOperation(this));
    }

    #endregion
}
