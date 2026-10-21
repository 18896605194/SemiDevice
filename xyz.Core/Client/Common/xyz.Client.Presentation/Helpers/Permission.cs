using System.Windows;
using xyz.Client.Common.Session;

namespace xyz.Client.Presentation.Helpers;

/// <summary>
/// 界面上的权限：页面能不能操作（壳设在页面最外层）、哪些控件要能操作才能用、按钮属于哪个功能项
/// </summary>
public static class Permission
{
    #region 页面能不能操作

    /// <summary>
    /// 页面能不能操作：壳按当前会话设在页面最外层，页面里的控件都读得到（往下传）
    /// </summary>
    public static readonly DependencyProperty CanOperateProperty = DependencyProperty.RegisterAttached(
        "CanOperate",
        typeof(bool),
        typeof(Permission),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits, OnCanOperateChanged));

    public static bool GetCanOperate(DependencyObject element)
    {
        return (bool)element.GetValue(CanOperateProperty);
    }

    public static void SetCanOperate(DependencyObject element, bool value)
    {
        element.SetValue(CanOperateProperty, value);
    }

    #endregion

    #region 要能操作才能用

    /// <summary>
    /// 页面里会改东西的按钮、输入框或者它们那一块标上 True：页面只看时它灰掉。本身绑了 IsEnabled 的控件别标，标在外面包一层上
    /// </summary>
    public static readonly DependencyProperty OperateProperty = DependencyProperty.RegisterAttached(
        "Operate",
        typeof(bool),
        typeof(Permission),
        new PropertyMetadata(false, OnOperateChanged));

    public static bool GetOperate(DependencyObject element)
    {
        return (bool)element.GetValue(OperateProperty);
    }

    public static void SetOperate(DependencyObject element, bool value)
    {
        element.SetValue(OperateProperty, value);
    }

    private static void OnCanOperateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (GetOperate(d))
        {
            ApplyOperate(d);
        }
    }

    private static void OnOperateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ApplyOperate(d);
    }

    private static void ApplyOperate(DependencyObject d)
    {
        var element = d as UIElement;
        if (element is null)
        {
            return;
        }

        if (GetOperate(element) && !GetCanOperate(element))
        {
            element.IsEnabled = false;
            return;
        }

        Restore(element);
    }

    /// <summary>
    /// 去掉这里设的灰：清掉本地值再让控件按自己的条件重算（命令能不能执行、外面灰没灰）。
    /// 只清不重算的话 IsEnabled 直接回到 true，命令点不了的按钮也会亮
    /// </summary>
    private static void Restore(UIElement element)
    {
        element.ClearValue(UIElement.IsEnabledProperty);
        element.CoerceValue(UIElement.IsEnabledProperty);
    }

    #endregion

    #region 功能项（按钮级）

    /// <summary>
    /// 按钮属于哪个功能项（整机控制、报警复位）：当前角色不允许就灰掉，换人登录跟着变。本身绑了 IsEnabled 的控件别用
    /// </summary>
    public static readonly DependencyProperty FunctionProperty = DependencyProperty.RegisterAttached(
        "Function",
        typeof(string),
        typeof(Permission),
        new PropertyMetadata(null, OnFunctionChanged));

    public static string? GetFunction(DependencyObject element)
    {
        return (string?)element.GetValue(FunctionProperty);
    }

    public static void SetFunction(DependencyObject element, string? value)
    {
        element.SetValue(FunctionProperty, value);
    }

    /// <summary>
    /// 标了功能项的控件：会话一变挨个重新判（弱引用，页面关了不留着）
    /// </summary>
    private static readonly List<WeakReference<UIElement>> FunctionElements = [];

    private static bool _subscribed;

    private static void OnFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = d as UIElement;
        if (element is null)
        {
            return;
        }

        if (!_subscribed)
        {
            _subscribed = true;
            ClientSession.Changed += OnSessionChanged;
        }

        FunctionElements.Add(new WeakReference<UIElement>(element));
        ApplyFunction(element);
    }

    private static void OnSessionChanged()
    {
        FunctionElements.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in FunctionElements)
        {
            if (reference.TryGetTarget(out var element))
            {
                ApplyFunction(element);
            }
        }
    }

    private static void ApplyFunction(UIElement element)
    {
        string? code = GetFunction(element);
        if (string.IsNullOrEmpty(code) || ClientSession.CanOperate(code))
        {
            Restore(element);
            return;
        }

        element.IsEnabled = false;
    }

    #endregion
}
