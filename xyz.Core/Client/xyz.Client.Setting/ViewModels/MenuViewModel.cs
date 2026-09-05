using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text.Json;
using xyz.Client.DataModels.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Setting.Models;
using xyz.Client.Setting.Views;
using xyz.Shared.Dtos;
using xyz.Shared.Services;

namespace xyz.Client.Setting.ViewModels;

/// <summary>
/// 菜单管理页面 ViewModel。
/// </summary>
public class MenuViewModel : BaseViewModel
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private List<MenuDto> _allMenus = new();

    #region Column

    public ObservableCollection<MenuDisplayModel> Menus { get; }

    private MenuDisplayModel? _selectedMenu;

    public MenuDisplayModel? SelectedMenu
    {
        get => _selectedMenu;
        set => SetProperty(ref _selectedMenu, value);
    }

    #endregion

    #region Command

    public IAsyncRelayCommand CreateMenuCommand { get; }

    public IAsyncRelayCommand DeleteMenuCommand { get; }

    public IAsyncRelayCommand SaveMenuCommand { get; }

    #endregion

    #region Service

    private readonly IMenuService _menuService;

    #endregion

    public MenuViewModel()
    {
        Menus = new ObservableCollection<MenuDisplayModel>();
        _menuService = GrpcClientFactory.Create<IMenuService>();

        CreateMenuCommand = new AsyncRelayCommand(DoCreateMenu);
        DeleteMenuCommand = new AsyncRelayCommand(DoDeleteMenu, CanDeleteMenu);
        SaveMenuCommand = new AsyncRelayCommand(DoSaveMenu, CanSaveMenu);
    }

    public override void Init()
    {
        RefreshMenus();
    }

    private bool CanDeleteMenu()
    {
        return SelectedMenu != null;
    }

    private bool CanSaveMenu()
    {
        return SelectedMenu != null;
    }

    private async Task DoCreateMenu()
    {
        var result = MenuDialogService.Show(_allMenus);
        if (result == null)
        {
            return;
        }

        await _menuService.CreateMenuAsync(new RpcRequest
        {
            Parameters = BuildParameters(result)
        });

        RefreshMenus();
    }

    private async Task DoSaveMenu()
    {
        if (SelectedMenu == null)
        {
            return;
        }

        var menuDto = _allMenus.FirstOrDefault(menu => menu.Id == SelectedMenu.Id);
        if (menuDto == null)
        {
            return;
        }

        var result = MenuDialogService.Show(_allMenus, menuDto);
        if (result == null)
        {
            return;
        }

        var parameters = BuildParameters(result);
        parameters["Id"] = SelectedMenu.Id.ToString();

        await _menuService.SaveMenuAsync(new RpcRequest
        {
            Parameters = parameters
        });

        RefreshMenus();
    }

    private async Task DoDeleteMenu()
    {
        if (SelectedMenu == null)
        {
            return;
        }

        await _menuService.DeleteMenuAsync(new RpcRequest
        {
            Parameters = new Dictionary<string, string>
            {
                ["Id"] = SelectedMenu.Id.ToString()
            }
        });

        RefreshMenus();
    }

    private void RefreshMenus()
    {
        var response = _menuService
            .GetMenusAsync(new RpcRequest())
            .GetAwaiter()
            .GetResult();

        _allMenus = Deserialize<List<MenuDto>>(response);

        var items = _allMenus
            .OrderBy(menu => menu.Sort)
            .Select(menu => new MenuDisplayModel
            {
                Id = menu.Id,
                Name = menu.Name,
                Code = menu.Code,
                ParentId = menu.ParentId,
                ParentName = menu.ParentId == null
                    ? "-"
                    : _allMenus.FirstOrDefault(parent => parent.Id == menu.ParentId)?.Name ?? "-",
                Sort = menu.Sort,
                IsEnabled = menu.IsEnabled
            })
            .ToList();

        foreach (var item in items)
        {
            if (item.ParentId != null)
            {
                var parent = items.FirstOrDefault(p => p.Id == item.ParentId);
                parent?.Children.Add(item);
            }
        }

        Menus.Clear();
        foreach (var item in items.Where(menu => menu.ParentId == null))
        {
            Menus.Add(item);
        }
    }

    private static Dictionary<string, string> BuildParameters(MenuEditResult result)
    {
        var parameters = new Dictionary<string, string>
        {
            ["Name"] = result.Name,
            ["Code"] = result.Code,
            ["Sort"] = result.Sort.ToString(),
            ["IsEnabled"] = result.IsEnabled.ToString()
        };

        if (result.ParentId != null)
        {
            parameters["ParentId"] = result.ParentId.Value.ToString();
        }

        return parameters;
    }

    private static T Deserialize<T>(RpcResponse response)
    {
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Message);
        }

        return JsonSerializer.Deserialize<T>(response.Data, JsonOptions)!;
    }
}
