using xyz._35021.Module.Drivers.Fcd;
using xyz._35021.Module.Drivers.Fcd.Commands;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Drivers.Communication;
using xyz.Drivers.Device;
using xyz.Modules;
using xyz.Shared.Dtos;

namespace xyz._35021.Module.Loadport;

/// <summary>
/// 35021 LoadPort 模块（富创得 LP300）：动作入口、扫片落账对账、在位判断、轮询节奏、推送都是平台的（BaseLoadPortModule），
/// 这里只写每个动作发哪几条 FCD 指令、一圈查什么、上报怎么认；要加互锁重写 XxxInterlock（先调 base）
/// </summary>
[Component(description: "35021 LoadPort 模块（富创得 LP300）")]
public class LoadPortModule : BaseLoadPortModule
{
    protected override IFrameCodec CreateCodec()
    {
        return new FcdCodec();
    }

    #region 动作

    /// <summary>
    /// 装载开门：CLOAD（只开门，不扫片）
    /// </summary>
    protected override CommandOperation AddLoadSteps(CommandOperation operation)
    {
        return operation.Then("开门", () => new FcdLoadCommand());
    }

    /// <summary>
    /// 关门：CULOD
    /// </summary>
    protected override CommandOperation AddUnloadSteps(CommandOperation operation)
    {
        return operation.Then("关门", () => new FcdUnloadCommand());
    }

    /// <summary>
    /// 自己扫片：MAPDO 扫，GET:MAPDT 取槽图存起来
    /// </summary>
    protected override CommandOperation AddMapSteps(CommandOperation operation)
    {
        return operation
            .Then("扫片", () => new FcdMapCommand())
            .Then("取槽图", () => new FcdGetMapCommand(), command =>
            {
                SaveMapResult(command.SlotMap);
                return HandleResult.Success();
            });
    }

    protected override ModuleOperation CreateHomeOperation()
    {
        return new CommandOperation("Home", _driver, () => new FcdHomeCommand(), () => HomeTimeout);
    }

    protected override ModuleOperation CreateClampOperation()
    {
        return new CommandOperation("Clamp", _driver, () => new FcdClampCommand(), () => ClampTimeout);
    }

    protected override ModuleOperation CreateUnclampOperation()
    {
        return new CommandOperation("Unclamp", _driver, () => new FcdUnclampCommand(), () => UnclampTimeout);
    }

    protected override ModuleOperation CreateResetOperation()
    {
        return new CommandOperation("Reset", _driver, () => new FcdResetCommand(), () => ResetTimeout);
    }

    protected override ModuleOperation CreateAbortOperation()
    {
        return new CommandOperation("Abort", _driver, () => new FcdAbortCommand(), () => AbortTimeout);
    }

    #endregion

    #region 状态查询和上报

    /// <summary>
    /// 一圈就一条 GET:STATE，查到的写进 Status
    /// </summary>
    protected override IEnumerable<DeviceCommand> QueryRound()
    {
        var state = new FcdGetStateCommand();
        yield return state;
        if (state.IsSuccess)
        {
            Status = state.Status;
        }
    }

    /// <summary>
    /// FCD 的主动事件：INF:PODON 放上、INF:PODOF 拿走（在位走设备上报时用）
    /// </summary>
    protected override void OnReport(object message)
    {
        if (message is FcdMessage fcd && fcd.Type == FcdProtocol.Inf)
        {
            switch (fcd.Name.ToUpperInvariant())
            {
                case "PODON":
                    SetDeviceReportedPlaced(true);
                    break;

                case "PODOF":
                    SetDeviceReportedPlaced(false);
                    break;

                default:
                    break;
            }
        }
    }

    #endregion
}
