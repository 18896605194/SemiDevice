using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

namespace xyz.Client.Manual.Models;

/// <summary>
/// 转台图四周的站点角标：设备站点号 + 名称 + 所在方位（机械手站点表推来，不变），
/// 外加站点模块自己的实时状态和站点上的片（Robot 页按站点名订阅 LoadPort / 腔体的状态推送，就地刷新；片以晶圆账为准）。
/// </summary>
public sealed class RobotStationModel : ObservableObject
{
    public string Name { get; init; } = string.Empty;

    /// <summary>设备站点号（sc.xml Number，如 LoadPort1=1、Chamber1=3）。</summary>
    public int Number { get; init; }

    /// <summary>机械手伸出方向（sc.xml Direction）。</summary>
    public RobotDirection Direction { get; init; }

    /// <summary>机械手伸出距离（sc.xml Y，数值）。</summary>
    public double Y { get; init; }

    /// <summary>站点槽数（站点模块在 sc.xml 里配的 SlotCount：LoadPort 25、腔体 1），取放槽位下拉按它列 1~N。</summary>
    public int SlotCount { get; init; }

    /// <summary>这个站点允许用的手指号（sc.xml 站点节点的 Arms，没配就是所有手指），取放手臂下拉只列这些。</summary>
    public IReadOnlyList<int> Arms { get; init; } = [];

    /// <summary>角标主文案：站点号。</summary>
    public string NumberText => Number.ToString();

    /// <summary>角标副文案：站点名。</summary>
    public string Title => Name;

    private string _stateText = string.Empty;

    /// <summary>站点模块的状态文字；还没收到状态推送为空（卡片上不显示徽标）。</summary>
    public string StateText
    {
        get => _stateText;
        private set => SetProperty(ref _stateText, value);
    }

    private ModuleStateTone _stateTone;

    /// <summary>站点模块的状态色调。</summary>
    public ModuleStateTone StateTone
    {
        get => _stateTone;
        private set => SetProperty(ref _stateTone, value);
    }

    /// <summary>
    /// 用站点模块的状态推送刷新（界面线程调用）。文字和色调由站点模块自己的显示模型归好——
    /// 同一个码在 LoadPort 和腔体里意思不同，这儿不认码。
    /// </summary>
    public void UpdateState(string text, ModuleStateTone tone)
    {
        StateText = text;
        StateTone = tone;
    }

    private WaferModel? _wafer;

    /// <summary>腔体站点上的片（晶圆账）；空片位或不是腔体为 null。调度图的腔体卡片画它。</summary>
    public WaferModel? Wafer
    {
        get => _wafer;
        private set => SetProperty(ref _wafer, value);
    }

    private IReadOnlyList<WaferModel> _wafers = [];

    /// <summary>LoadPort 站点花篮里的片（以晶圆账为准）；调度图的 LoadPort 卡片画它。</summary>
    public IReadOnlyList<WaferModel> Wafers
    {
        get => _wafers;
        private set => SetProperty(ref _wafers, value);
    }

    /// <summary>用腔体的状态推送刷新卡片上的片（界面线程调用）。</summary>
    public void UpdateWafer(WaferModel? wafer)
    {
        Wafer = wafer;
    }

    /// <summary>用 LoadPort 的状态推送刷新卡片上的花篮（界面线程调用）。</summary>
    public void UpdateWafers(IReadOnlyList<WaferModel> wafers)
    {
        Wafers = wafers;
    }
}
