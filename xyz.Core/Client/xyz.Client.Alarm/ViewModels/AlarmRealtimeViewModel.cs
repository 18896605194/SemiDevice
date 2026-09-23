using System.Collections.ObjectModel;
using xyz.Client.Common.Alarms;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Alarm.ViewModels;

/// <summary>
/// 实时报警页 ViewModel（只显示）：跟着客户端当前报警（ClientAlarms）走，最新的在最上面。
/// </summary>
public class AlarmRealtimeViewModel : BaseViewModel
{
    #region Column

    /// <summary>
    /// 当前报警，最新的在最前。
    /// </summary>
    public ObservableCollection<AlarmModel> Alarms { get; } = [];

    #endregion

    public override void Init()
    {
        ClientAlarms.Changed -= Refresh;
        ClientAlarms.Changed += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        Alarms.Clear();
        foreach (var dto in ClientAlarms.Active)
        {
            Alarms.Add(AlarmModel.From(dto));
        }
    }
}
