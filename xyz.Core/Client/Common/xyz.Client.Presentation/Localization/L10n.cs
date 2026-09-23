using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace xyz.Client.Presentation.Localization;

/// <summary>
/// 语言包查询：按 key 从当前合并资源字典（平台包 + 机型包）取句子模板并格式化参数。
/// 缺 key 回退 key 本身，保证不显示空白。XAML 里用 {DynamicResource key} 绑定同源文案。
/// 语言包文件一律叫 Localization/Strings.{语言}.xaml：平台的在本程序集，机型的在机型程序集。
/// </summary>
public static class L10n
{
    /// <summary>
    /// 默认界面语言：简体中文（App.xaml 里先合并的就是它）。
    /// </summary>
    public const string DefaultLanguage = "zh-CN";

    /// <summary>
    /// 当前界面语言，如 zh-CN、en-US。
    /// </summary>
    public static string Language { get; private set; } = DefaultLanguage;

    /// <summary>
    /// 取当前语言的句子；args 填充模板 {0}{1} 占位。
    /// </summary>
    public static string Get(string key, params object?[] args)
    {
        var template = Find(key);
        if (string.IsNullOrEmpty(template))
        {
            return key;
        }

        return args.Length == 0 ? template : string.Format(template, args);
    }

    /// <summary>
    /// 按错误码参数列表取句子（如 RpcResponse.Args）：展开成逐个参数再填占位。
    /// 列表直接传给 params 版会被当成一个参数，句子里只剩类型名，两个占位还会抛 FormatException。
    /// </summary>
    public static string Get(string key, IEnumerable<string> args)
    {
        return Get(key, args.Cast<object?>().ToArray());
    }

    /// <summary>
    /// 取句子模板，没有这个 key 返回 null（给有后备文字的地方用）。
    /// </summary>
    public static string? Find(string key)
    {
        return Application.Current?.TryFindResource(key) as string;
    }

    /// <summary>
    /// 菜单显示名：语言包里的 menu.{Code}；没配就显示 Code 本身，一眼能看出漏配了哪个。
    /// 框架菜单配在平台包，机型独有菜单配在机型包。
    /// </summary>
    public static string MenuName(string code)
    {
        return Find("menu." + code) ?? code;
    }

    /// <summary>
    /// 切换界面语言：把 Application 资源里的各语言包（平台和各机型的）换成目标语言的，
    /// 目标语言缺哪个包就保留原来的，界面不会出现空白。要在建界面之前调（启动时按后端 sc.xml 的 System 节点设）。
    /// </summary>
    public static void Apply(string language)
    {
        if (string.IsNullOrWhiteSpace(language) || Application.Current is null)
        {
            return;
        }

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString;
            if (source is null || !source.EndsWith(PackFileName(Language), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = source[..^PackFileName(Language).Length] + PackFileName(language);
            if (TryLoad(target) is { } pack)
            {
                dictionaries[i] = pack;
            }
        }

        Language = language;

        // WPF 控件自带的文字（如日期控件的"显示日历"）跟界面文化走，不跟着换就还是操作系统的语言。
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            // 不认识的语言名：只换语言包
            return;
        }

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;

        // 在 async 方法里设文化只管那一段异步流程；UI 线程上的布局、渲染是另外调度的，
        // 要在调度操作里设一次，WPF 会把调度操作里改的文化留在线程上，之后的调度都用它。
        Application.Current.Dispatcher.BeginInvoke(
            DispatcherPriority.Send,
            () => CultureInfo.CurrentUICulture = culture);
    }

    /// <summary>
    /// 合并一个程序集（机型模块）的语言包：按当前语言加载，没有就退回默认语言的，都没有就跳过。
    /// </summary>
    public static void AddPack(string assemblyName)
    {
        if (Application.Current is null)
        {
            return;
        }

        var prefix = $"pack://application:,,,/{assemblyName};component";
        var pack = TryLoad(prefix + PackFileName(Language)) ?? TryLoad(prefix + PackFileName(DefaultLanguage));
        if (pack is not null)
        {
            Application.Current.Resources.MergedDictionaries.Add(pack);
        }
    }

    private static string PackFileName(string language)
    {
        return $"/Localization/Strings.{language}.xaml";
    }

    private static ResourceDictionary? TryLoad(string source)
    {
        try
        {
            return new ResourceDictionary { Source = new Uri(source, UriKind.RelativeOrAbsolute) };
        }
        catch (Exception)
        {
            // 这个语言没有语言包
            return null;
        }
    }
}
