using System.Diagnostics;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Drivers.Loadport;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz._35021.Module.Loadport.Operation;

namespace xyz._35021.Module.Loadport;

/// <summary>
/// 35021 机台 LoadPort 模块：各动作操作 + 设备状态轮询（操作类在 Operation 文件夹）。
/// 品牌驱动是 sc.xml 挂在本模块下的 Driver 子组件，换 Type 即换品牌。
/// </summary>
[Component(description: "35021 LoadPort 模块")]
public class LoadPortModule : BaseLoadPortModule, ILoadPort
{
    #region Mapping

    /// <summary>
    /// Load 操作收到 Mapping 数据后调用：转交基类更新 SlotMap 并回调 EAP。
    /// </summary>
    internal void NoteSlotMap(IReadOnlyList<SlotState> slotMap)
    {
        UpdateSlotMap(slotMap);
    }

    #endregion

    #region 扫描

    private ActionStep _stateQueryStep = ActionStep.SendCommand;
    private LoadPortCommand? _stateCommand;
    private readonly Stopwatch _stateQueryWatch = new();

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
                _stateCommand = driver.QueryStatus();
                if (_stateCommand is not null)
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
                        // 驱动保留旧查询的在途项，旧回复到达前不会受理下一条状态查询。
                        LogHelper.Warn(Name, $"状态查询超时（{timeout}ms）");
                    }

                    break;
                }

                var response = _stateCommand.Response!;
                if (response.IsSuccess && response.Status is not null)
                {
                    Status = response.Status;
                }

                _stateCommand = null;
                _stateQueryWatch.Reset();
                _stateQueryStep = ActionStep.SendCommand;
                break;
        }
    }

    #endregion

    #region Action

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

    protected override ModuleOperation? ResetDevice()
    {
        return Begin(LoadPortAction.Reset, new ResetOperation(this));
    }

    protected override ModuleOperation? AbortDevice()
    {
        return Begin(LoadPortAction.Abort, new AbortOperation(this));
    }

    public override ModuleOperation? Clamp()
    {
        return Begin(LoadPortAction.Clamp, new ClampOperation(this));
    }

    public override ModuleOperation? Unclamp()
    {
        return Begin(LoadPortAction.Unclamp, new UnclampOperation(this));
    }

    #endregion
}
