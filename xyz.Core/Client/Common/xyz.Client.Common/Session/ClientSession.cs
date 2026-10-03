namespace xyz.Client.Common.Session;

/// <summary>
/// 当前登录的人。登录还没做，先固定是 Admin；做登录时在这里换成登录的人——
/// 顶栏显示的用户、要记操作人的地方（账单调整）都从这儿取，不各写各的。
/// </summary>
public static class ClientSession
{
    /// <summary>
    /// 当前用户名。
    /// </summary>
    public static string UserName { get; } = "Admin";
}
