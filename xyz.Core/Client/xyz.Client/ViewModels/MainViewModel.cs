using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using xyz.Client.Common.Log;
using xyz.Client.Common.Session;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Models;
using xyz.Client.Modules;
using xyz.Client.Presentation.Helpers;
using xyz.Client.Presentation.Localization;
using xyz.Client.Views;
using xyz.Shared.Dtos;

namespace xyz.Client.ViewModels;

/// <summary>
/// 主界面 ViewModel。菜单写在代码里：平台菜单（PlatformMenuProvider）+ 机型模块声明的菜单，名字走语言包；
/// 按模块生成的菜单（IO、腔体手动）直接显示模块名。底部只放当前角色看得到的菜单，换人登录重排（页面不重建）。
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
            if (!SetProperty(ref _selectedSecondaryMenu, value) || value is null)
            {
                return;
            }

            if (SelectedPrimaryMenu is not null)
            {
                _lastSecondaryCodes[SelectedPrimaryMenu.Code] = value.Code;
            }

            ShowPage(value);
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

    private IReadOnlyList<UserControl> _pages = [];

    /// <summary>
    /// 启动时建好的全部页面：主窗口内容区一次性挂上、一起排好版，切菜单只切可见性（见 PageHost）。
    /// </summary>
    public IReadOnlyList<UserControl> Pages
    {
        get => _pages;
        private set => SetProperty(ref _pages, value);
    }

    private bool _isCurrentPageViewOnly;

    /// <summary>
    /// 当前角色对当前这一页只能看（底部最右边显示"这一页只看"）
    /// </summary>
    public bool IsCurrentPageViewOnly
    {
        get => _isCurrentPageViewOnly;
        private set => SetProperty(ref _isCurrentPageViewOnly, value);
    }

    private string _viewOnlyText = string.Empty;

    /// <summary>
    /// "这一页只看 · 角色"
    /// </summary>
    public string ViewOnlyText
    {
        get => _viewOnlyText;
        private set => SetProperty(ref _viewOnlyText, value);
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

    /// <summary>
    /// 声明的全部菜单（不管当前角色看不看得到）：页面照它全部建好，换人只重排底部菜单
    /// </summary>
    private List<ClientMenu> _menus = [];

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

        ClientSession.Changed -= OnSessionChanged;
        ClientSession.Changed += OnSessionChanged;
    }

    /// <summary>
    /// 换人登录、注销、权限变了：页面的灰亮跟着变，底部菜单重排
    /// </summary>
    private void OnSessionChanged()
    {
        ApplyPagePermissions();
        BuildMenus();
    }

    /// <summary>
    /// 换一级菜单：列出它的二级页面；没有二级页面直接显示它自己的页面，
    /// 有就回到上次在它下面看的那一页（第一次来是第一页），高亮的菜单跟中间的页面始终对得上。
    /// </summary>
    private void ApplyPrimaryMenuSelection(MenuModel? menu)
    {
        SecondaryMenus.Clear();
        SelectedSecondaryMenu = null;

        if (menu is not null)
        {
            foreach (var child in menu.Children)
            {
                SecondaryMenus.Add(child);
            }
        }

        OnPropertyChanged(nameof(HasSecondaryMenus));

        if (menu is null)
        {
            return;
        }

        if (SecondaryMenus.Count == 0)
        {
            ShowPage(menu);
            return;
        }

        var lastCode = _lastSecondaryCodes.GetValueOrDefault(menu.Code);
        SelectedSecondaryMenu = SecondaryMenus.FirstOrDefault(child => child.Code == lastCode) ?? SecondaryMenus[0];
    }

    /// <summary>
    /// 显示一页，底部的"这一页只看"跟着它
    /// </summary>
    private void ShowPage(MenuModel menu)
    {
        CurrentView = CreateView(menu);
        IsCurrentPageViewOnly = menu.IsViewOnly;
        ViewOnlyText = L10n.Get("account.view_only_chip", RoleText.Of(ClientSession.RoleKind, ClientSession.RoleName));
    }

    /// <summary>
    /// 收齐菜单、建好全部页面：平台先注册，同一个 Code 只认第一个声明的；父菜单没人声明的二级菜单记日志跳过。
    /// </summary>
    private void LoadMenus()
    {
        _menus = _menuProviders
            .SelectMany(provider => provider.Menus)
            .DistinctBy(menu => menu.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var codes = _menus.Select(menu => menu.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var orphan in _menus.Where(menu => menu.ParentCode is not null && !codes.Contains(menu.ParentCode)))
        {
            ClientLog.Warn("Client", $"菜单 {orphan.Code} 的父菜单 {orphan.ParentCode} 没有声明，已跳过。");
        }

        PreloadViews(_menus);
        Pages = _views.Values.ToList();
        ApplyPagePermissions();
        BuildMenus();
    }

    /// <summary>
    /// 按当前会话合成底部菜单：隐藏的不放，一级菜单下的页面都看不到就整个不放；原来那一页还在就停在那一页，不在了回第一个
    /// </summary>
    private void BuildMenus()
    {
        string? primaryCode = SelectedPrimaryMenu?.Code;
        string? secondaryCode = SelectedSecondaryMenu?.Code;
        if (primaryCode is not null && secondaryCode is not null)
        {
            _lastSecondaryCodes[primaryCode] = secondaryCode;
        }

        SelectedPrimaryMenu = null;
        PrimaryMenus.Clear();
        foreach (var menu in _menus
                     .Where(menu => menu.ParentCode is null)
                     .OrderBy(menu => menu.Sort))
        {
            var model = BuildMenu(menu);
            if (model is not null)
            {
                PrimaryMenus.Add(model);
            }
        }

        SelectedPrimaryMenu = PrimaryMenus.FirstOrDefault(menu => menu.Code == primaryCode) ?? PrimaryMenus.FirstOrDefault();
    }

    /// <summary>
    /// 一个菜单和它下面的：当前角色看不到返回 null
    /// </summary>
    private MenuModel? BuildMenu(ClientMenu menu)
    {
        var menuModel = new MenuModel
        {
            Name = menu.Title ?? L10n.MenuName(menu.Code),
            Code = menu.Code,
            IsViewOnly = MenuPermissions.IsViewOnly(menu)
        };

        var children = _menus
            .Where(child => string.Equals(child.ParentCode, menu.Code, StringComparison.OrdinalIgnoreCase))
            .OrderBy(child => child.Sort)
            .ToList();
        if (children.Count == 0)
        {
            return MenuPermissions.LevelOf(menu) == PermissionLevel.Hidden ? null : menuModel;
        }

        foreach (var child in children)
        {
            var childModel = BuildMenu(child);
            if (childModel is not null)
            {
                menuModel.Children.Add(childModel);
            }
        }

        return menuModel.Children.Count == 0 ? null : menuModel;
    }

    /// <summary>
    /// 给每个页面挂"能不能操作"：页面里标了要能操作的按钮、输入框跟着灰亮（没有改东西操作的页一律算能）
    /// </summary>
    private void ApplyPagePermissions()
    {
        foreach (var menu in _menus)
        {
            if (_views.TryGetValue(menu.Code, out var view))
            {
                bool canOperate = menu.Access == MenuAccess.ViewOnly || MenuPermissions.LevelOf(menu) == PermissionLevel.Operate;
                Permission.SetCanOperate(view, canOperate);
            }
        }
    }

    private void PreloadViews(IEnumerable<ClientMenu> menus)
    {
        foreach (var menu in menus)
        {
            var view = _serviceProvider.GetKeyedService<UserControl>(menu.Code);
            if (view is not null)
            {
                _views[menu.Code] = view;
            }
        }
    }

    private object CreateView(MenuModel menu)
    {
        if (!_views.TryGetValue(menu.Code, out var view))
        {
            // 还没实现的页面给个占位页：建一次记下来，来回切不重复建（内容区会一直挂着它）。
            view = new PlaceholderView
            {
                Title = menu.Name
            };
            _views[menu.Code] = view;
        }

        return view;
    }

}
