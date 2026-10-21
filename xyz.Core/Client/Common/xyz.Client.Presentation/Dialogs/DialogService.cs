using System.Collections;
using System.Windows;
using xyz.Client.Presentation.Controls;

namespace xyz.Client.Presentation.Dialogs;

/// <summary>
/// 通用弹窗服务。
/// </summary>
public static class DialogService
{
    /// <summary>
    /// 弹出公共确认弹窗：标题、说明、确定按钮的字由调用方给。danger = 确定按钮用红色（中止、删除这类）；
    /// showCancel = false 时只有一个按钮（只是告知）。点确定返回 true，取消、Esc 返回 false。
    /// </summary>
    public static bool ShowConfirm(string title, string message, string okText, bool danger = false, bool showCancel = true)
    {
        var dialog = new ConfirmDialog(title, message, okText, danger, showCancel)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true;
    }

    /// <summary>
    /// 弹出公共选择弹窗：标题、列、数据由调用方给，单选。返回选中的那一项；取消返回 null。
    /// 给了 currentValue、valuePath 就先选中值对得上的那一项（如选择框里现在填的）。
    /// </summary>
    public static object? ShowPicker(
        string title,
        IEnumerable<PickerColumn> columns,
        IEnumerable items,
        string? currentValue = null,
        string? valuePath = null)
    {
        var dialog = new PickerDialog(title, columns, items, currentValue, valuePath)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true ? dialog.SelectedItem : null;
    }

    /// <summary>
    /// 弹出登录框：login 拿登录名、密码去登录，成功回 null，没成回原因（框不关）。登录成功返回 true，取消返回 false。
    /// showGuestHint = true 时多一行"取消就当访客"的说明（开机弹的那次）
    /// </summary>
    public static bool ShowLogin(string userName, bool showGuestHint, Func<string, string, Task<string?>> login)
    {
        var dialog = new LoginDialog(userName, showGuestHint, login)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true;
    }

    /// <summary>
    /// 弹出改密码 / 重置密码框：needOld = 要旧密码；submit 拿旧密码、新密码去改，成功回 null，没成回原因（框不关）。改成了返回 true
    /// </summary>
    public static bool ShowPassword(string title, string subtitle, bool needOld, Func<string, string, Task<string?>> submit)
    {
        var dialog = new PasswordDialog(title, subtitle, needOld, submit)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true;
    }

    /// <summary>
    /// 弹出新建 / 改账号框：roles 是选角色的候选（要有 DisplayName、Description）；submit 成功回 null，没成回原因（框不关）。建成、改成返回 true
    /// </summary>
    public static bool ShowUser(string title, UserForm initial, bool isNew, bool canChangeRole, IEnumerable roles,
        Func<UserForm, Task<string?>> submit)
    {
        var dialog = new UserDialog(title, initial, isNew, canChangeRole, roles, submit)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true;
    }

    /// <summary>
    /// 弹出新建 / 改角色框：submit 拿名字、说明去建 / 改，成功回 null，没成回原因（框不关）。建成、改成返回 true
    /// </summary>
    public static bool ShowRole(string title, string name, string description, Func<string, string, Task<string?>> submit)
    {
        var dialog = new RoleDialog(title, name, description, submit)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true;
    }
}
