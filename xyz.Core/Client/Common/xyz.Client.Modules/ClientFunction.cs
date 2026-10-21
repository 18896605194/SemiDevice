namespace xyz.Client.Modules;

/// <summary>
/// 功能项声明：不按页面管、要单独给开关的一组按钮（整机控制、报警复位），只有允许 / 不允许两档
/// </summary>
public sealed class ClientFunction
{
    public ClientFunction(string parentCode, string code, int sort)
    {
        ParentCode = parentCode;
        Code = code;
        Sort = sort;
    }

    /// <summary>
    /// 挂在哪个菜单下面（角色页里排在它后面）
    /// </summary>
    public string ParentCode { get; }

    /// <summary>
    /// 功能项编码；显示名在语言包里配 permission.{Code}
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// 同一个菜单下的先后
    /// </summary>
    public int Sort { get; }
}
