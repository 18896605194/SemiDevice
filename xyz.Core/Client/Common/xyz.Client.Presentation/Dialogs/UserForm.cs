namespace xyz.Client.Presentation.Dialogs;

/// <summary>
/// 账号框里填的：登录名、显示名、角色（显示名）、密码（改账号时为空）
/// </summary>
public sealed record UserForm(string UserName, string DisplayName, string RoleName, string Password);
