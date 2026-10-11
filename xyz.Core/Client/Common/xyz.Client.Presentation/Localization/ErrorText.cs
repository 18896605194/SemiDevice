using System.Globalization;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Client.Presentation.Localization;

/// <summary>
/// 后端回包的失败原因在当前语言里的文字，各页面共用：有错误码按语言包翻，没错误码给 Message。
/// "当前状态不能做"（module.state_not_allowed）的第二个参数是状态码，同一个码在不同模块意思不同（LoadPort 110 已装载 / 腔体 110 工艺中），
/// 所以由调用方给这个模块的状态字翻法（如 ModuleStates.LoadPortText），界面上不露数字。
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

        return L10n.Get(response.Code, args);
    }
}
