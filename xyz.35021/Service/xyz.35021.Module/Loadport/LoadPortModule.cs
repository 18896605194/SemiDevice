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
    protected override void AddLoadSteps(ModuleRoutine load)
    {
        load.Command("开门", () => new FcdLoadCommand());
    }

    /// <summary>
    /// 关门：CULOD
    /// </summary>
    protected override void AddUnloadSteps(ModuleRoutine unload)
    {
        unload.Command("关门", () => new FcdUnloadCommand());
    }

    /// <summary>
    /// 自己扫片：MAPDO 扫，GET:MAPDT 取槽图，连同设备回的原文存起来
    /// </summary>
    protected override void AddMapSteps(ModuleRoutine map)
    {
        map.Command("扫片", () => new FcdMapCommand())
            .Command("取槽图", () => new FcdGetMapCommand(), command =>
            {
                SaveMapResult(command.SlotMap, command.Raw);
                return HandleResult.Success();
            });
    }

    /// <summary>
    /// 整机回零：MOV:ORGSH
    /// </summary>
    protected override void AddHomeSteps(ModuleRoutine home)
    {
        home.Command("回零", () => new FcdHomeCommand());
    }

    /// <summary>
    /// 夹紧 FOUP：MOV:PODCL
    /// </summary>
    protected override void AddClampSteps(ModuleRoutine clamp)
    {
        clamp.Command("夹紧", () => new FcdClampCommand());
    }

    /// <summary>
    /// 松开 FOUP
    /// </summary>
    protected override void AddUnclampSteps(ModuleRoutine unclamp)
    {
        unclamp.Command("松开", () => new FcdUnclampCommand());
    }

    /// <summary>
    /// 复位清错：SET:RESET
    /// </summary>
    protected override void AddResetSteps(ModuleRoutine reset)
    {
        reset.Command("清报错", () => new FcdResetCommand());
    }

    /// <summary>
    /// 终止当前动作：MOV:ABORT
    /// </summary>
    protected override void AddAbortSteps(ModuleRoutine abort)
    {
        abort.Command("急停", () => new FcdAbortCommand());
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
