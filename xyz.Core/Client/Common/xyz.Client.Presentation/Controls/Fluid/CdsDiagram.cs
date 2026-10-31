using System.Windows;

namespace xyz.Client.Presentation.Controls.Fluid;

/// <summary>仅供布局编辑器识别元件和接线；显示由普通 XAML 和控件依赖属性完成。</summary>
public static class CdsDiagram
{
    public static readonly DependencyProperty ComponentRootProperty = Register("ComponentRoot");
    public static readonly DependencyProperty IdProperty = Register("Id");
    public static readonly DependencyProperty KindProperty = Register("Kind");
    public static readonly DependencyProperty ComponentProperty = Register("Component");
    public static readonly DependencyProperty FromProperty = Register("From");
    public static readonly DependencyProperty ToProperty = Register("To");
    public static readonly DependencyProperty RouteKindProperty = Register("RouteKind");
    public static readonly DependencyProperty TextProperty = Register("Text");
    private static DependencyProperty Register(string name) => DependencyProperty.RegisterAttached(name, typeof(string), typeof(CdsDiagram), new PropertyMetadata(""));
    public static string GetComponentRoot(DependencyObject d) => (string)d.GetValue(ComponentRootProperty);
    public static void SetComponentRoot(DependencyObject d, string value) => d.SetValue(ComponentRootProperty, value);
    public static string GetId(DependencyObject d) => (string)d.GetValue(IdProperty);
    public static void SetId(DependencyObject d, string value) => d.SetValue(IdProperty, value);
    public static string GetKind(DependencyObject d) => (string)d.GetValue(KindProperty);
    public static void SetKind(DependencyObject d, string value) => d.SetValue(KindProperty, value);
    public static string GetComponent(DependencyObject d) => (string)d.GetValue(ComponentProperty);
    public static void SetComponent(DependencyObject d, string value) => d.SetValue(ComponentProperty, value);
    public static string GetFrom(DependencyObject d) => (string)d.GetValue(FromProperty);
    public static void SetFrom(DependencyObject d, string value) => d.SetValue(FromProperty, value);
    public static string GetTo(DependencyObject d) => (string)d.GetValue(ToProperty);
    public static void SetTo(DependencyObject d, string value) => d.SetValue(ToProperty, value);
    public static string GetRouteKind(DependencyObject d) => (string)d.GetValue(RouteKindProperty);
    public static void SetRouteKind(DependencyObject d, string value) => d.SetValue(RouteKindProperty, value);
    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);
}
