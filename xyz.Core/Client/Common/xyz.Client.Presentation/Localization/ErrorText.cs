using System.Globalization;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Client.Presentation.Localization;

/// <summary>
/// 后端回包的失败原因在当前语言里的文字，各页面共用：有错误码按语言包翻，没错误码给 Message
/// </summary>
public static class ErrorText
{
    public static string Of(RpcResponse response, Func<int, string> stateText)
    {
        if (string.IsNullOrEmpty(response.Code))
        {
            return response.Message;
        }

        var args = response.Args.ToList();
        if (response.Code == ErrorCodes.ModuleStateNotAllowed && args.Count > 1
            && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int state))
        {
            args[1] = stateText(state);
        }

        return StepPrefix(response) + L10n.Get(response.Code, args);
    }

    /// <summary>
    /// 不带模块状态码的回包（账号、角色这些）
    /// </summary>
    public static string Of(RpcResponse response)
    {
        if (string.IsNullOrEmpty(response.Code))
        {
            return response.Message;
        }

        return StepPrefix(response) + L10n.Get(response.Code, response.Args);
    }

    /// <summary>
    /// 动作停在第几步（不止一步的动作才带）："第 3/5 步："；没带为空
    /// </summary>
    private static string StepPrefix(RpcResponse response)
    {
        if (response.StepCount <= 1 || response.StepNumber <= 0)
        {
            return string.Empty;
        }

        return L10n.Get("routine.step_at", response.StepNumber, response.StepCount);
    }
}
