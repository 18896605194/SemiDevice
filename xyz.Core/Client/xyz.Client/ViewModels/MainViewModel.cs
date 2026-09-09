using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using xyz.Shared.Rpc;
using System.Windows.Controls;
using xyz.Client.DataModels.Rpc;
using xyz.Client.Setting.Models;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Views;
using xyz.Shared.Dtos;
using xyz.Shared.Services;

namespace xyz.Client.ViewModels;

/// <summary>
/// 主界面 ViewModel。菜单数据来自后端 Menu 表。
/// </summary>
public class MainViewModel : BaseViewModel
{
    #region Column

    /// <summary>
    /// 一级菜单。
    /// </summary>
    public ObservableCollection<MenuModel> PrimaryMenus { get; }

    /// <summary>
    /// 当前选中的一级菜单对应的二级菜单。
    /// </summary>
    public ObservableCollection<MenuModel> SecondaryMenus { get; }

    private MenuModel? _selectedPrimaryMenu;

    /// <summary>
    /// 当前选中的一级菜单。
    /// </summary>
    public MenuModel? SelectedPrimaryMenu
    {
        get => _selectedPrimaryMenu;
        set
        {
            if (SetProperty(ref _selectedPrimaryMenu, value))
            {
                ApplyPrimaryMenuSelection(value);
            }
        }
    }

    private MenuModel? _selectedSecondaryMenu;

    /// <summary>
    /// 当前选中的二级菜单。
    /// </summary>
    public MenuModel? SelectedSecondaryMenu
    {
        get => _selectedSecondaryMenu;
        set
        {
            if (!SetProperty(ref _selectedSecondaryMenu, value) || value == null)
            {
                return;
            }

            CurrentView = CreateView(value);
        }
    }

    private bool _isSecondaryMenuOpen;

    /// <summary>
    /// 是否显示当前一级菜单对应的二级菜单浮层。
    /// </summary>
    public bool IsSecondaryMenuOpen
    {
        get => _isSecondaryMenuOpen;
        set => SetProperty(ref _isSecondaryMenuOpen, value);
    }

    private object? _currentView;

    /// <summary>
    /// 中间内容区域当前显示的内容。
    /// </summary>
    public object? CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    #endregion

    #region Command


    #endregion

    #region Service

    private readonly IServiceProvider _serviceProvider;
    private readonly IMenuService _menuService;
    private readonly Dictionary<string, UserControl> _views = new(StringComparer.OrdinalIgnoreCase);

    #endregion

    public MainViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _menuService = GrpcClientFactory.Create<IMenuService>();

        PrimaryMenus = new ObservableCollection<MenuModel>();
        SecondaryMenus = new ObservableCollection<MenuModel>();
    }

    public override void Init()
    {
        PrimaryMenus.Clear();
        SecondaryMenus.Clear();
        SelectedPrimaryMenu = null;
        SelectedSecondaryMenu = null;
        CurrentView = null;
        IsSecondaryMenuOpen = false;

        LoadMenus();
    }

    private void ApplyPrimaryMenuSelection(MenuModel? menu)
    {
        SecondaryMenus.Clear();
        SelectedSecondaryMenu = null;

        if (menu?.Children == null)
        {
            return;
        }

        foreach (var child in menu.Children)
        {
            SecondaryMenus.Add(child);
        }
    }

    /// <summary>
    /// 点击一级菜单时，在底部菜单上方临时展开它的二级菜单。
    /// </summary>
    public void OpenPrimaryMenu(MenuModel menu)
    {
        if (!ReferenceEquals(SelectedPrimaryMenu, menu))
        {
            SelectedPrimaryMenu = menu;
        }

        if (SecondaryMenus.Count == 0)
        {
            CurrentView = CreateView(menu);
            IsSecondaryMenuOpen = false;
            return;
        }

        IsSecondaryMenuOpen = true;
    }

    /// <summary>
    /// 选中二级菜单后显示对应页面，并收起浮层。
    /// </summary>
    public void SelectSecondaryMenu(MenuModel menu)
    {
        if (!ReferenceEquals(SelectedSecondaryMenu, menu))
        {
            SelectedSecondaryMenu = menu;
        }
        else
        {
            CurrentView = CreateView(menu);
        }

        IsSecondaryMenuOpen = false;
    }

    private void LoadMenus()
    {
        var response = _menuService
            .GetMenusAsync(new RpcRequest())
            .GetAwaiter()
            .GetResult();

        var menuDtos = response.DeserializeData<List<MenuDto>>();

        PrimaryMenus.Clear();
        PreloadViews(menuDtos);

        foreach (var menuDto in menuDtos
                     .Where(menu => menu.ParentId == null)
                     .OrderBy(menu => menu.Sort))
        {
            PrimaryMenus.Add(BuildMenu(menuDto, menuDtos));
        }

        SelectedPrimaryMenu = PrimaryMenus.FirstOrDefault();
    }

    private static MenuModel BuildMenu(MenuDto menuDto, List<MenuDto> allMenus)
    {
        var menuModel = new MenuModel
        {
            Name = menuDto.Name,
            Code = menuDto.Code
        };

        foreach (var child in allMenus
                     .Where(menu => menu.ParentId == menuDto.Id)
                     .OrderBy(menu => menu.Sort))
        {
            menuModel.Children.Add(BuildMenu(child, allMenus));
        }

        return menuModel;
    }

    private void PreloadViews(List<MenuDto> menuDtos)
    {
        foreach (var menuDto in menuDtos)
        {
            var view = _serviceProvider.GetKeyedService<UserControl>(menuDto.Code);
            if (view != null)
            {
                _views[menuDto.Code] = view;
            }
        }
    }

    private object CreateView(MenuModel menu)
    {
        if (_views.TryGetValue(menu.Code, out var view))
        {
            return view;
        }

        return new PlaceholderView
        {
            Title = menu.Name
        };
    }

}
