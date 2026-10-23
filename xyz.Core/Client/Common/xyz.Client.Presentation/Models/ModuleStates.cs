using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 模块状态码 → 状态字（按当前语言）和色调（状态徽标的底色）。
/// 同一个码在不同模块意思不同（LoadPort 110 已装载 / 腔体 110 工艺中），所以按模块种类分开查。
/// 码值对应 xyz.Modules 的 ModuleState / TransferModuleState 及各模块自己的状态，未收录的码显示原值。
/// 手动页的模块卡片、机械手页的站点卡片、顶栏的模块行都用这一份。
/// </summary>
public static class ModuleStates
{
    /// <summary>
    /// 按模块种类查状态字（顶栏模块行这类各种模块混在一起显示的地方用）
    /// </summary>
    public static string TextOf(StationKind kind, int state)
    {
        switch (kind)
        {
            case StationKind.LoadPort:
                return LoadPortText(state);
            case StationKind.Chamber:
                return ChamberText(state);
            case StationKind.Robot:
                return RobotText(state);
            case StationKind.Aligner:
                return AlignerText(state);
            default:
                return OtherText(state);
        }
    }

    /// <summary>
    /// 按模块种类查色调
    /// </summary>
    public static ModuleStateTone ToneOf(StationKind kind, int state)
    {
        switch (kind)
        {
            case StationKind.LoadPort:
                return LoadPortTone(state);
            case StationKind.Chamber:
                return ChamberTone(state);
            case StationKind.Robot:
                return RobotTone(state);
            case StationKind.Aligner:
                return AlignerTone(state);
            default:
                return OtherTone(state);
        }
    }

    /// <summary>
    /// LoadPort：已装载是它的待命态（可被机械手服务），跟空闲一样算就绪；装卸、回零、夹紧/松开、扫片、传片环都算动作中。
    /// </summary>
    public static string LoadPortText(int state)
    {
        switch (state)
        {
            case 100:
                return L10n.Get("module.state.loading");
            case 110:
                return L10n.Get("module.state.loaded");
            case 120:
                return L10n.Get("module.state.unloading");
            case 130:
                return L10n.Get("module.state.homing");
            case 140:
                return L10n.Get("module.state.clamping");
            case 150:
                return L10n.Get("module.state.unclamping");
            case 160:
                return L10n.Get("module.state.mapping");
            default:
                return CommonText(state);
        }
    }

    public static ModuleStateTone LoadPortTone(int state)
    {
        switch (state)
        {
            case 110:
                return ModuleStateTone.Ready;
            case 100:
            case 120:
            case 130:
            case 140:
            case 150:
            case 160:
                return ModuleStateTone.Busy;
            default:
                return CommonTone(state);
        }
    }

    /// <summary>
    /// 腔体：空闲就绪；回零、工艺、部件手动动作都算动作中。
    /// </summary>
    public static string ChamberText(int state)
    {
        switch (state)
        {
            case 100:
                return L10n.Get("module.state.homing");
            case 110:
                return L10n.Get("module.state.processing");
            case 120:
                return L10n.Get("module.state.manual");
            default:
                return CommonText(state);
        }
    }

    public static ModuleStateTone ChamberTone(int state)
    {
        switch (state)
        {
            case 100:
            case 110:
            case 120:
                return ModuleStateTone.Busy;
            default:
                return CommonTone(state);
        }
    }

    /// <summary>
    /// 机械手：空闲就绪；回零、取放、扫片都算动作中，分段取放伸着停在站里（211 / 221）也算。
    /// </summary>
    public static string RobotText(int state)
    {
        switch (state)
        {
            case 130:
            case 200:
                return L10n.Get("module.state.homing");
            case 210:
                return L10n.Get("module.state.picking");
            case 211:
                return L10n.Get("module.state.pick_extended");
            case 220:
                return L10n.Get("module.state.placing");
            case 221:
                return L10n.Get("module.state.place_extended");
            case 230:
                return L10n.Get("module.state.mapping");
            default:
                return CommonText(state);
        }
    }

    public static ModuleStateTone RobotTone(int state)
    {
        switch (state)
        {
            case 130:
            case 200:
            case 210:
            case 211:
            case 220:
            case 221:
            case 230:
                return ModuleStateTone.Busy;
            default:
                return CommonTone(state);
        }
    }

    /// <summary>
    /// 整机（主界面的系统状态）：未初始化、初始化中、空闲、出错跟模块一样；35 是整机停止的"停止中"，
    /// 另有 100 自动空闲、110 自动运行、120 全部回片中（xyz.Modules 的 SystemState）。
    /// </summary>
    public static string SystemText(int state)
    {
        switch (state)
        {
            case 35:
                return L10n.Get("module.state.stopping");
            case 100:
                return L10n.Get("module.state.auto_idle");
            case 110:
                return L10n.Get("module.state.auto_running");
            case 120:
                return L10n.Get("module.state.returning");
            default:
                return CommonText(state);
        }
    }

    /// <summary>
    /// 整机：空闲、自动空闲就绪；初始化、自动运行、回片算动作中；停止中黄、出错红、未初始化灰。
    /// </summary>
    public static ModuleStateTone SystemTone(int state)
    {
        switch (state)
        {
            case 100:
                return ModuleStateTone.Ready;
            case 110:
            case 120:
                return ModuleStateTone.Busy;
            default:
                return CommonTone(state);
        }
    }

    /// <summary>
    /// 整机在不在 Auto：自动空闲、自动运行。
    /// </summary>
    public static bool IsSystemAuto(int state)
    {
        return state == 100 || state == 110;
    }

    /// <summary>
    /// 对准器：回零、对准算动作中
    /// </summary>
    public static string AlignerText(int state)
    {
        switch (state)
        {
            case 100:
                return L10n.Get("module.state.homing");
            case 110:
                return L10n.Get("module.state.aligning");
            default:
                return CommonText(state);
        }
    }

    public static ModuleStateTone AlignerTone(int state)
    {
        switch (state)
        {
            case 100:
            case 110:
                return ModuleStateTone.Busy;
            default:
                return CommonTone(state);
        }
    }

    /// <summary>
    /// 其他模块（Buffer、供液这类）：只认通用状态和传片环。
    /// </summary>
    public static string OtherText(int state)
    {
        return CommonText(state);
    }

    public static ModuleStateTone OtherTone(int state)
    {
        return CommonTone(state);
    }

    /// <summary>
    /// 各模块共用的码：初始化、空闲、中止、报错，以及传片环（搬运前、待搬运、搬运中、搬运完成）。
    /// </summary>
    private static string CommonText(int state)
    {
        switch (state)
        {
            case 10:
                return L10n.Get("module.state.not_init");
            case 20:
                return L10n.Get("module.state.initing");
            case 30:
                return L10n.Get("module.state.idle");
            case 35:
                return L10n.Get("module.state.aborting");
            case 40:
                return L10n.Get("module.state.error");
            case 50:
                return L10n.Get("module.state.pre_transfer");
            case 60:
                return L10n.Get("module.state.transfer_ready");
            case 70:
                return L10n.Get("module.state.transferring");
            case 80:
                return L10n.Get("module.state.transfer_complete");
            default:
                return L10n.Get("module.state.unknown", state);
        }
    }

    private static ModuleStateTone CommonTone(int state)
    {
        switch (state)
        {
            case 30:
                return ModuleStateTone.Ready;
            case 35:
                return ModuleStateTone.Warning;
            case 40:
                return ModuleStateTone.Alarm;
            case 20:
            case 50:
            case 60:
            case 70:
            case 80:
                return ModuleStateTone.Busy;
            default:
                return ModuleStateTone.Inactive;
        }
    }
}
