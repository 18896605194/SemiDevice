using xyz.Client.Presentation.Localization;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 模块状态码 → 状态字（按当前语言）和色调（状态徽标的底色）。
/// 同一个码在不同模块意思不同（LoadPort 110 已装载 / 腔体 110 工艺中），所以按模块种类分开查。
/// 码值对应 xyz.Modules 的 ModuleState / TransferModuleState 及各模块自己的状态，未收录的码显示原值。
/// 手动页的模块卡片、机械手页的站点卡片都用这一份。
/// </summary>
public static class ModuleStates
{
    /// <summary>
    /// LoadPort：已装载是它的锚点态（可被机械手服务），跟空闲一样算就绪；装卸、回零、夹紧/松开、传片环都算动作中。
    /// </summary>
    public static string LoadPortText(int state)
    {
        return state switch
        {
            100 => L10n.Get("module.state.loading"),
            110 => L10n.Get("module.state.loaded"),
            120 => L10n.Get("module.state.unloading"),
            130 => L10n.Get("module.state.homing"),
            140 => L10n.Get("module.state.clamping"),
            150 => L10n.Get("module.state.unclamping"),
            _ => CommonText(state),
        };
    }

    public static ModuleStateTone LoadPortTone(int state)
    {
        return state switch
        {
            110 => ModuleStateTone.Ready,
            100 or 120 or 130 or 140 or 150 => ModuleStateTone.Busy,
            _ => CommonTone(state),
        };
    }

    /// <summary>
    /// 腔体：空闲就绪；回零、工艺都算动作中。
    /// </summary>
    public static string ChamberText(int state)
    {
        return state switch
        {
            100 => L10n.Get("module.state.homing"),
            110 => L10n.Get("module.state.processing"),
            _ => CommonText(state),
        };
    }

    public static ModuleStateTone ChamberTone(int state)
    {
        return state switch
        {
            100 or 110 => ModuleStateTone.Busy,
            _ => CommonTone(state),
        };
    }

    /// <summary>
    /// 机械手：空闲就绪；回零、取放都算动作中。
    /// </summary>
    public static string RobotText(int state)
    {
        return state switch
        {
            130 or 200 => L10n.Get("module.state.homing"),
            210 => L10n.Get("module.state.picking"),
            220 => L10n.Get("module.state.placing"),
            _ => CommonText(state),
        };
    }

    public static ModuleStateTone RobotTone(int state)
    {
        return state switch
        {
            130 or 200 or 210 or 220 => ModuleStateTone.Busy,
            _ => CommonTone(state),
        };
    }

    /// <summary>
    /// 其他模块（Aligner、Buffer 这类）：只认通用状态和传片环。
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
        return state switch
        {
            10 => L10n.Get("module.state.not_init"),
            20 => L10n.Get("module.state.initing"),
            30 => L10n.Get("module.state.idle"),
            35 => L10n.Get("module.state.aborting"),
            40 => L10n.Get("module.state.error"),
            50 => L10n.Get("module.state.pre_transfer"),
            60 => L10n.Get("module.state.transfer_ready"),
            70 => L10n.Get("module.state.transferring"),
            80 => L10n.Get("module.state.transfer_complete"),
            _ => L10n.Get("module.state.unknown", state),
        };
    }

    private static ModuleStateTone CommonTone(int state)
    {
        return state switch
        {
            30 => ModuleStateTone.Ready,
            35 => ModuleStateTone.Warning,
            40 => ModuleStateTone.Alarm,
            20 or 50 or 60 or 70 or 80 => ModuleStateTone.Busy,
            _ => ModuleStateTone.Inactive,
        };
    }
}
