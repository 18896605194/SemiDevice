using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Client.Presentation.ViewModels;

/// <summary>
/// 调度图（DispatchMap）自带的 ViewModel：一台机械手 + 它站点表里的各个站点。
/// 全靠订推送：机械手的状态（含站点表）、每个站点模块的状态都是留存消息，订上就补发最后一条，断线重连后也会重放，不用另外拉。
/// 站点表一变（后端搬运模块表绑好后槽数、类型才齐）就按新的表重订站点。
/// </summary>
public sealed class DispatchMapViewModel : BaseViewModel, IDisposable
{
    #region Column

    /// <summary>
    /// 机械手模块名，也是它状态推送的 token（如 Robot1）。
    /// </summary>
    public string RobotName { get; }

    /// <summary>
    /// 机械手显示模型：转台、手臂、手指上的片画它，站点按它的站点表摆。
    /// </summary>
    public RobotModel Robot { get; } = new();

    #endregion

    #region Service

    private IDisposable? _robotSubscription;

    /// <summary>站点卡片的状态订阅：每个站点订 LoadPort、腔体两种推送（站点是哪种就只会来哪种）。</summary>
    private readonly List<IDisposable> _stationSubscriptions = [];

    /// <summary>当前订着的是哪一份站点表（站点表没变时不换实例，按引用比）。</summary>
    private IReadOnlyList<RobotStationModel>? _subscribedStations;

    #endregion

    public DispatchMapViewModel(string robotName)
    {
        RobotName = robotName;
    }

    public override void Init()
    {
        _robotSubscription?.Dispose();
        _robotSubscription = EventBus.Register<RobotDto>(RobotName, OnRobotState);
    }

    public void Dispose()
    {
        _robotSubscription?.Dispose();
        _robotSubscription = null;
        UnsubscribeStations();
    }

    private void OnRobotState(RobotDto dto)
    {
        Robot.Update(dto);
        SubscribeStations();
    }

    /// <summary>
    /// 按站点名（就是模块名，也是推送的 token）订站点模块的状态，刷新卡片上的状态徽标、片和灯。站点表没换就不重订。
    /// </summary>
    private void SubscribeStations()
    {
        var stations = Robot.StationMarks;
        if (ReferenceEquals(stations, _subscribedStations))
        {
            return;
        }

        UnsubscribeStations();
        _subscribedStations = stations;

        foreach (var station in stations)
        {
            _stationSubscriptions.Add(EventBus.Register<LoadPortDto>(station.Name, station.UpdateLoadPort));
            _stationSubscriptions.Add(EventBus.Register<ChamberDto>(station.Name, station.UpdateChamber));
        }
    }

    private void UnsubscribeStations()
    {
        foreach (var subscription in _stationSubscriptions)
        {
            subscription.Dispose();
        }

        _stationSubscriptions.Clear();
        _subscribedStations = null;
    }
}
