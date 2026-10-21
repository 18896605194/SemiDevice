using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.Setting.Models;

/// <summary>
/// 角色页权限表的一行：一级菜单、一项、功能项或只给管理员的项；三个单选框绑 IsHidden / IsView / IsOperate
/// </summary>
public class PermissionRowModel : ObservableObject
{
    public PermissionRowModel(string code, string name, string tip, PermissionRowKind kind, bool canHide, bool canOperate)
    {
        Code = code;
        Name = name;
        Tip = tip;
        Kind = kind;
        CanHide = canHide;
        CanOperateLevel = canOperate;
        _level = PermissionLevel.View;
    }

    #region Column

    /// <summary>
    /// 权限项编码；一级菜单那一行是菜单 Code（不存）
    /// </summary>
    public string Code { get; }

    public string Name { get; }

    /// <summary>
    /// 管哪些操作
    /// </summary>
    public string Tip { get; }

    public PermissionRowKind Kind { get; }

    /// <summary>
    /// 能不能选隐藏（主界面、功能项不能）
    /// </summary>
    public bool CanHide { get; }

    /// <summary>
    /// 有没有"能操作"这一档（报警、数据中心这些只能看的页没有）
    /// </summary>
    public bool CanOperateLevel { get; }

    public bool IsGroup => Kind == PermissionRowKind.Group;

    public bool IsFunction => Kind == PermissionRowKind.Function;

    public bool IsAdminOnly => Kind == PermissionRowKind.AdminOnly;

    /// <summary>
    /// 放隐藏、只看两个单选框（功能项是"不允许 / 允许"，只给管理员的合成一句）
    /// </summary>
    public bool ShowHidden => !IsFunction && !IsAdminOnly;

    /// <summary>
    /// 第三格放能操作单选框
    /// </summary>
    public bool ShowOperate => ShowHidden && CanOperateLevel;

    /// <summary>
    /// 第三格放一道横线：这一项没有"能操作"这一档
    /// </summary>
    public bool ShowOperateDash => ShowHidden && !CanOperateLevel;

    /// <summary>
    /// 下面各项（一级菜单那一行才有）
    /// </summary>
    public List<PermissionRowModel> Children { get; } = [];

    /// <summary>
    /// 这一项挂在哪个一级菜单下面（改了它，那一行跟着刷新）
    /// </summary>
    public PermissionRowModel? Group { get; set; }

    private PermissionLevel _level;

    /// <summary>
    /// 档位（一级菜单那一行看下面各项）
    /// </summary>
    public PermissionLevel Level
    {
        get => _level;
        set
        {
            var level = Fit(value);
            if (!SetProperty(ref _level, level))
            {
                return;
            }

            RaiseLevelChanged();
            Group?.RaiseLevelChanged();
            LevelChanged?.Invoke();
        }
    }

    public bool IsHidden
    {
        get => Is(PermissionLevel.Hidden);
        set
        {
            if (value)
            {
                Choose(PermissionLevel.Hidden);
            }
        }
    }

    public bool IsView
    {
        get => Is(PermissionLevel.View);
        set
        {
            if (value)
            {
                Choose(PermissionLevel.View);
            }
        }
    }

    public bool IsOperate
    {
        get => Is(PermissionLevel.Operate);
        set
        {
            if (value)
            {
                Choose(PermissionLevel.Operate);
            }
        }
    }

    /// <summary>
    /// 一级菜单下面各项档位不一样
    /// </summary>
    public bool IsMixed => IsGroup && Children.Select(child => child.Level).Distinct().Count() > 1;

    private bool _isEditable = true;

    /// <summary>
    /// 能不能改（管理员角色的全锁上）
    /// </summary>
    public bool IsEditable
    {
        get => _isEditable;
        set
        {
            if (SetProperty(ref _isEditable, value))
            {
                OnPropertyChanged(nameof(CanChooseHidden));
            }
        }
    }

    /// <summary>
    /// 隐藏单选框能不能点
    /// </summary>
    public bool CanChooseHidden => IsEditable && CanHide;

    /// <summary>
    /// 档位改了（页面据此标"有修改没保存"）
    /// </summary>
    public event Action? LevelChanged;

    #endregion

    /// <summary>
    /// 不经"改了"的通知直接摆档位（换角色、撤销时整表重摆）
    /// </summary>
    public void Reset(PermissionLevel level)
    {
        _level = Fit(level);
        RaiseLevelChanged();
        Group?.RaiseLevelChanged();
    }

    private bool Is(PermissionLevel level)
    {
        if (IsGroup)
        {
            return Children.Count > 0 && Children.All(child => child.Level == level);
        }

        return _level == level;
    }

    /// <summary>
    /// 点了一档：一级菜单那一行把下面各项一起改（各项按自己能选的收）
    /// </summary>
    private void Choose(PermissionLevel level)
    {
        if (!IsEditable)
        {
            return;
        }

        if (!IsGroup)
        {
            Level = level;
            return;
        }

        foreach (var child in Children)
        {
            child.Level = level;
        }

        RaiseLevelChanged();
    }

    /// <summary>
    /// 收成这一项能选的：不能隐藏的至少只看，没有"能操作"的最多只看
    /// </summary>
    private PermissionLevel Fit(PermissionLevel level)
    {
        if (level == PermissionLevel.Hidden && !CanHide)
        {
            return PermissionLevel.View;
        }

        if (level == PermissionLevel.Operate && !CanOperateLevel && !IsFunction)
        {
            return PermissionLevel.View;
        }

        return level;
    }

    private void RaiseLevelChanged()
    {
        OnPropertyChanged(nameof(IsHidden));
        OnPropertyChanged(nameof(IsView));
        OnPropertyChanged(nameof(IsOperate));
        OnPropertyChanged(nameof(IsMixed));
    }
}
