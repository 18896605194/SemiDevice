namespace xyz.Shared.Permissions;

/// <summary>
/// 平台的权限项编码：菜单的就是菜单 Code，客户端菜单和后端预置角色共用这一份
/// </summary>
public static class PermissionCodes
{
    #region 菜单

    /// <summary>
    /// 主界面
    /// </summary>
    public const string Main = "Main";

    /// <summary>
    /// 手动（一级菜单）
    /// </summary>
    public const string Manual = "Manual";

    /// <summary>
    /// 手动 · LoadPort
    /// </summary>
    public const string ManualLoadPorts = "Manual.LoadPorts";

    /// <summary>
    /// 手动 · 机械手
    /// </summary>
    public const string ManualRobot = "Manual.Robot";

    /// <summary>
    /// 手动 · 腔体：所有腔体的手动页合成这一项
    /// </summary>
    public const string ManualChamber = "Manual.Chamber";

    /// <summary>
    /// 配方（一级菜单）
    /// </summary>
    public const string Recipe = "Recipe";

    /// <summary>
    /// 配方 · 流程配方
    /// </summary>
    public const string RecipeSequence = "Recipe.Sequence";

    /// <summary>
    /// 配方 · 工艺配方
    /// </summary>
    public const string RecipeProcess = "Recipe.Process";

    /// <summary>
    /// 报警（一级菜单）
    /// </summary>
    public const string Alarm = "Alarm";

    /// <summary>
    /// 报警 · 实时报警
    /// </summary>
    public const string AlarmRealtime = "Alarm.Realtime";

    /// <summary>
    /// 报警 · 报警历史
    /// </summary>
    public const string AlarmHistory = "Alarm.History";

    /// <summary>
    /// 数据中心（一级菜单）
    /// </summary>
    public const string DataCenter = "DataCenter";

    /// <summary>
    /// 数据中心 · 实时日志
    /// </summary>
    public const string DataCenterLogRealtime = "DataCenter.LogRealtime";

    /// <summary>
    /// 数据中心 · 日志历史
    /// </summary>
    public const string DataCenterLogHistory = "DataCenter.LogHistory";

    /// <summary>
    /// 数据中心 · 数据曲线
    /// </summary>
    public const string DataCenterDataChart = "DataCenter.DataChart";

    /// <summary>
    /// 数据中心 · 实时曲线
    /// </summary>
    public const string DataCenterRealChart = "DataCenter.RealChart";

    /// <summary>
    /// 数据中心 · Job 历史
    /// </summary>
    public const string DataCenterJobHistory = "DataCenter.JobHistory";

    /// <summary>
    /// 设置（一级菜单）
    /// </summary>
    public const string Setting = "Setting";

    /// <summary>
    /// 设置 · EC
    /// </summary>
    public const string SettingEc = "Setting.Ec";

    /// <summary>
    /// 设置 · 账单调整
    /// </summary>
    public const string SettingWaferLedger = "Setting.WaferLedger";

    /// <summary>
    /// 设置 · 用户管理：固定只给管理员
    /// </summary>
    public const string SettingUser = "Setting.User";

    /// <summary>
    /// 设置 · 角色管理：固定只给管理员
    /// </summary>
    public const string SettingRole = "Setting.Role";

    /// <summary>
    /// IO：每个模块的 IO 页合成这一项
    /// </summary>
    public const string Io = "Io";

    #endregion

    #region 功能项

    /// <summary>
    /// 整机控制：主界面的 Auto、Manual、Stop、初始化、全部回片、切晶圆尺寸
    /// </summary>
    public const string MainSystem = "Main.System";

    /// <summary>
    /// 报警复位：顶栏的复位、主界面的 Reset
    /// </summary>
    public const string AlarmReset = "Alarm.Reset";

    #endregion
}
