using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using xyz.Client.Common.Log;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Models;
using xyz.Client.Modules;
using xyz.Client.Presentation.Localization;
using xyz.Client.Views;

namespace xyz.Client.ViewModels;

/// <summary>
/// 主界面 ViewModel。菜单写在代码里：平台菜单（PlatformMenuProvider）+ 机型模块声明的菜单，名字走语言包。
/// </summary>
public class MainViewModel : BaseViewModel
{
    #region Column

    /// <summary>
    /// 顶栏右侧：四色灯、当前时间、整机复位。
    /// </summary>
    public TopBarViewModel TopBar { get; }

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
    /// 当前选中的一级菜单：选中即显示它的页面；有二级页面时回到上次在它下面看的那一页，第一次来显示第一页。
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
    /// 当前选中的二级菜单：选中即显示它的页面，并记下来，切回这个一级菜单时还回到这一页。
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

            if (SelectedPrimaryMenu != null)
            {
                _lastSecondaryCodes[SelectedPrimaryMenu.Code] = value.Code;
            }

            CurrentView = CreateView(value);
        }
    }

    /// <summary>
    /// 当前一级菜单下有没有二级页面（底部菜单里的竖线跟着它显示）。
    /// </summary>
    public bool HasSecondaryMenus => SecondaryMenus.Count > 0;

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
    private readonly IReadOnlyList<IClientMenuProvider> _menuProviders;
    private readonly Dictionary<string, UserControl> _views = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 每个一级菜单上次看的二级页面：一级菜单 Code → 二级菜单 Code。
    /// </summary>
    private readonly Dictionary<string, string> _lastSecondaryCodes = new(StringComparer.OrdinalIgnoreCase);

    #endregion

    public MainViewModel(IServiceProvider serviceProvider, TopBarViewModel topBar, IEnumerable<IClientMenuProvider> menuProviders)
    {
        _serviceProvider = serviceProvider;
        TopBar = topBar;
        _menuProviders = menuProviders.ToList();

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

        LoadMenus();
    }

    /// <summary>
    /// 换一级菜单：列出它的二级页面；没有二级页面直接显示它自己的页面，
    /// 有就回到上次在它下面看的那一页（第一次来是第一页），高亮的菜单跟中间的页面始终对得上。
    /// </summary>
    private void ApplyPrimaryMenuSelection(MenuModel? menu)
    {
        SecondaryMenus.Clear();
        SelectedSecondaryMenu = null;

        if (menu != null)
        {
            foreach (var child in menu.Children)
            {
                SecondaryMenus.Add(child);
            }
        }

        OnPropertyChanged(nameof(HasSecondaryMenus));

        if (menu == null)
        {
            return;
        }

        if (SecondaryMenus.Count == 0)
        {
            CurrentView = CreateView(menu);
            return;
        }

        var lastCode = _lastSecondaryCodes.GetValueOrDefault(menu.Code);
        SelectedSecondaryMenu = SecondaryMenus.FirstOrDefault(child => child.Code == lastCode) ?? SecondaryMenus[0];
    }

    /// <summary>
    /// 合成菜单树：平台先注册，同一个 Code 只认第一个声明的；父菜单没人声明的二级菜单记日志跳过。
    /// </summary>
    private void LoadMenus()
    {
        var menus = _menuProviders
            .SelectMany(provider => provider.Menus)
            .DistinctBy(menu => menu.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var codes = menus.Select(menu => menu.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var orphan in menus.Where(menu => menu.ParentCode != null && !codes.Contains(menu.ParentCode)))
        {
            ClientLog.Warn("Client", $"菜单 {orphan.Code} 的父菜单 {orphan.ParentCode} 没有声明，已跳过。");
        }

        PreloadViews(menus);

        foreach (var menu in menus
                     .Where(menu => menu.ParentCode == null)
                     .OrderBy(menu => menu.Sort))
        {
            PrimaryMenus.Add(BuildMenu(menu, menus));
        }

        SelectedPrimaryMenu = PrimaryMenus.FirstOrDefault();
    }

    private static MenuModel BuildMenu(ClientMenu menu, IReadOnlyList<ClientMenu> allMenus)
    {
        var menuModel = new MenuModel
        {
            Name = L10n.MenuName(menu.Code),
            Code = menu.Code
        };

        foreach (var child in allMenus
                     .Where(child => string.Equals(child.ParentCode, menu.Code, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(child => child.Sort))
        {
            menuModel.Children.Add(BuildMenu(child, allMenus));
        }

        return menuModel;
    }

    private void PreloadViews(IEnumerable<ClientMenu> menus)
    {
        foreach (var menu in menus)
        {
            var view = _serviceProvider.GetKeyedService<UserControl>(menu.Code);
            if (view != null)
            {
                _views[menu.Code] = view;
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
