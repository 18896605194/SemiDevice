namespace xyz.Elements.Models;


public sealed record DataSignal(
    string Name,
    bool IsDigital,
    string Source,
    string Address,
    string Unit,
    string Description,
    Func<double?> Read)
{
    /// <summary>
    /// 信号名的层级分隔符：Chamber1__Door__DiOpened。信号名就是库里的列名，SqlSugar 遇到点号会拆成"表.列"、
    /// 写库时还拿列名当参数名，所以层级之间用两个下划线（客户端勾选树 SignalTree 按同一个符号分层）。
    /// </summary>
    public const string Separator = "__";
}
