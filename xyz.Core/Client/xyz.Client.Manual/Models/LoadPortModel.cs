using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Localization;

namespace xyz.Client.Manual.Models;

/// <summary>
/// LoadPort 手动界面的显示模型，由 LoadPortDto 经 Mapster 映射，字段与 DTO 同名对齐。
/// </summary>
public class LoadPortModel : ObservableObject
{
    private int _state;

    /// <summary>模块状态码，取值见 ModuleState/LoadPortState。</summary>
    public int State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    private bool _isConnected;

    /// <summary>驱动串口连接是否可用。</summary>
    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    private bool _isPodPlaced;

    /// <summary>FOUP 在位（状态查询或 PODON/PODOF 事件）。</summary>
    public bool IsPodPlaced
    {
        get => _isPodPlaced;
        set => SetProperty(ref _isPodPlaced, value);
    }

    private bool? _podPresent;

    /// <summary>查询反馈：FOUP 在位；null 表示反馈不可用。</summary>
    public bool? PodPresent
    {
        get => _podPresent;
        set => SetProperty(ref _podPresent, value);
    }

    private bool? _podPlaced;

    /// <summary>查询反馈：FOUP 放置到位；null 表示反馈不可用。</summary>
    public bool? PodPlaced
    {
        get => _podPlaced;
        set => SetProperty(ref _podPlaced, value);
    }

    private bool? _deviceAlarm;

    /// <summary>查询反馈：设备硬件报警；null 表示反馈不可用。</summary>
    public bool? DeviceAlarm
    {
        get => _deviceAlarm;
        set => SetProperty(ref _deviceAlarm, value);
    }

    private bool? _autoMode;

    /// <summary>查询反馈：自动模式（E84 online），false 为手动；null 表示反馈不可用。</summary>
    public bool? AutoMode
    {
        get => _autoMode;
        set => SetProperty(ref _autoMode, value);
    }

    /// <summary>
    /// 状态文字（按当前语言）。码值对应 xyz.Modules 的
    /// ModuleState/TransferModuleState/LoadPortState，未收录的码显示原值。
    /// Model 每次整体替换，绑定随 Model 属性变化重新取值，无需单独通知。
    /// </summary>
    public string StateText
    {
        get
        {
            return State switch
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
                100 => L10n.Get("module.state.loading"),
                110 => L10n.Get("module.state.loaded"),
                120 => L10n.Get("module.state.unloading"),
                130 => L10n.Get("module.state.homing"),
                _ => L10n.Get("module.state.unknown", State),
            };
        }
    }
}
