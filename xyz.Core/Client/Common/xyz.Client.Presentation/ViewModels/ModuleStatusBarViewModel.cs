using System.Collections.ObjectModel;
using xyz.Client.Common.Events;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Client.Presentation.ViewModels;

/// <summary>
/// 顶栏模块行 ViewModel：订整机推的各模块状态（留存消息，订上就补发、重连后重放），断开时全部显示未连接
/// </summary>
public sealed class ModuleStatusBarViewModel : BaseViewModel
{
    #region Column

    /// <summary>
    /// 各模块，先后同 sc.xml
    /// </summary>
    public ObservableCollection<ModuleStatusModel> Modules { get; } = [];

    #endregion

    #region Service

    private IDisposable? _subscription;

    #endregion

    public override void Init()
    {
        _subscription?.Dispose();
        _subscription = EventBus.Register<ModuleStatusListDto>(ModuleStatusListDto.EventToken, OnModulesChanged);

        RemoteEventBus.ConnectionChanged -= OnConnectionChanged;
        RemoteEventBus.ConnectionChanged += OnConnectionChanged;
        OnConnectionChanged(RemoteEventBus.IsConnected);
    }

    private void OnConnectionChanged(bool connected)
    {
        if (connected)
        {
            return;
        }

        foreach (var module in Modules)
        {
            module.SetDisconnected();
        }
    }

    /// <summary>
    /// 模块表跟 sc 走、开机后不变，就地刷新；名字或先后对不上（后端换了 sc 重启过）才重建
    /// </summary>
    private void OnModulesChanged(ModuleStatusListDto list)
    {
        var dtos = list.Modules;
        if (!IsSameModules(dtos))
        {
            Modules.Clear();
            foreach (var dto in dtos)
            {
                Modules.Add(new ModuleStatusModel { Name = dto.Name });
            }
        }

        for (int i = 0; i < dtos.Count; i++)
        {
            Modules[i].Update(dtos[i]);
        }
    }

    private bool IsSameModules(List<ModuleStatusDto> dtos)
    {
        if (dtos.Count != Modules.Count)
        {
            return false;
        }

        for (int i = 0; i < dtos.Count; i++)
        {
            if (dtos[i].Name != Modules[i].Name)
            {
                return false;
            }
        }

        return true;
    }
}
