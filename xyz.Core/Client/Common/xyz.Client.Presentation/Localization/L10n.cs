using System.Windows;

namespace xyz.Client.Presentation.Localization;

/// <summary>
/// 语言包查询：按 key 从当前合并资源字典（平台包 + 机型包）取句子模板并格式化参数。
/// 缺 key 回退 key 本身，保证不显示空白。XAML 里用 {DynamicResource key} 绑定同源文案。
/// </summary>
public static class L10n
{
    /// <summary>
    /// 取当前语言的句子；args 填充模板 {0}{1} 占位。
    /// </summary>
    public static string Get(string key, params object?[] args)
    {
        var template = Application.Current?.TryFindResource(key) as string;
        if (string.IsNullOrEmpty(template))
        {
            return key;
        }

        return args.Length == 0 ? template : string.Format(template, args);
    }
}
