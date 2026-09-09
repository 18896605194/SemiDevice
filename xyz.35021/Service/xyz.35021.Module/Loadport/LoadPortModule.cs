using System.Diagnostics;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Drivers.Communication;
using xyz.Drivers.Loadport;
using xyz.Drivers.Loadport.FCD;
using xyz.Drivers.Loadport.FCD.Commands;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz._35021.Module.Loadport.Operation;

namespace xyz._35021.Module.Loadport;

/// <summary>
/// 35021 机台 LoadPort 模块：FCD 驱动 + 各动作操作（操作类在 Operation 文件夹）。
/// </summary>
[Component(description: "35021 LoadPort 模块")]
public class LoadPortModule : BaseLoadPortModule, ILoadPort
{
    private ActionStep _stateQueryStep = ActionStep.SendCommand;
    private FcdGetStateCommand? _stateCommand;
    private readonly Stopwatch _stateQueryWatch = new();

    /// <summary>
    /// 最近一次 Load 的 Mapping 槽位数据（如 25 个 P）。
    /// </summary>
    public string SlotMap { get; internal set; } = string.Empty;

    protected override LoadPortDriverBase CreateDriver()
    {
        return new FcdLoadPortDriver(new FrameCommunication(CreateTransport(), new FcdFrameCodec()));
    }

    protected override void OnScan()
    {
        base.OnScan();
        ScanDeviceStatus();
        PublishState();
    }

    /// <summary>
    /// 分周期下发状态查询并读取结果，不阻塞扫描线程。
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

        switch (_stateQueryStep)
        {
            case ActionStep.SendCommand:
                if (!driver.IsConnected)
                {
                    break;
                }

                _stateCommand = new FcdGetStateCommand(driver);
                if (_stateCommand.Execute())
                {
                    _stateQueryWatch.Restart();
                    _stateQueryStep = ActionStep.WaitCommand;
                }

                break;

            case ActionStep.WaitCommand:
                if (!_stateCommand!.IsCompleted)
                {
                    var timeout = QueryDataTimeOut;
                    if (_stateQueryWatch.ElapsedMilliseconds >= timeout)
                    {
                        Status = null;
                        _stateQueryWatch.Reset();
                        _stateCommand = null;
                        _stateQueryStep = ActionStep.SendCommand;
                        // 驱动保留旧查询的在途项，旧回复到达前不会受理下一条 STATE。
                        LogHelper.Warn(Name, $"GET:STATE 查询超时（{timeout}ms）");
                    }

                    break;
                }

                if (_stateCommand.IsSucceeded)
                {
                    var status = _stateCommand.Status;
                    if (status is not null)
                    {
                        Status = status;
                    }
                }

                _stateCommand = null;
                _stateQueryWatch.Reset();
                _stateQueryStep = ActionStep.SendCommand;
                break;
        }
    }

    public override ModuleOperation? Load()
    {
        return Begin(LoadPortAction.Load, new LoadOperation(this));
    }

    public override ModuleOperation? Unload()
    {
        return Begin(LoadPortAction.Unload, new UnloadOperation(this));
    }

    public override ModuleOperation? Home()
    {
        return Begin(LoadPortAction.Home, new HomeOperation(this));
    }

    public override ModuleOperation? Reset()
    {
        return Begin(LoadPortAction.Reset, new ResetOperation(this));
    }

    public override ModuleOperation? Abort()
    {
        return Begin(LoadPortAction.Abort, new AbortOperation(this));
    }
}
