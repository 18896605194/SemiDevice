namespace xyz.Shared.Rpc;

public static class RpcParams
{
    #region 通用

    /// <summary>
    /// 模块名（LoadPort1、Robot1、Chamber1）
    /// </summary>
    public const string Module = "Module";

    /// <summary>
    /// 编号（工艺配方、流程配方的编号；IO 的点号）
    /// </summary>
    public const string Index = "Index";

    /// <summary>
    /// 名称（配方、PJ、CJ、角色）
    /// </summary>
    public const string Name = "Name";

    /// <summary>
    /// 库里的编号（账号、角色）
    /// </summary>
    public const string Id = "Id";

    /// <summary>
    /// 说明
    /// </summary>
    public const string Description = "Description";

    /// <summary>
    /// 原因（人工调账）
    /// </summary>
    public const string Reason = "Reason";

    /// <summary>
    /// 来源（报警来源路径；手动传片的源站点）
    /// </summary>
    public const string Source = "Source";

    /// <summary>
    /// 槽号
    /// </summary>
    public const string Slot = "Slot";

    /// <summary>
    /// 手指号
    /// </summary>
    public const string Arm = "Arm";

    /// <summary>
    /// 值（EC 的新值、IO 写的值）
    /// </summary>
    public const string Value = "Value";

    #endregion

    #region 按时间段查

    /// <summary>
    /// 起始时刻（含，本机时间）
    /// </summary>
    public const string Start = "Start";

    /// <summary>
    /// 截止时刻（不含，本机时间）
    /// </summary>
    public const string End = "End";

    /// <summary>
    /// 只查这个等级 / 级别
    /// </summary>
    public const string Level = "Level";

    /// <summary>
    /// 关键字
    /// </summary>
    public const string Keyword = "Keyword";

    /// <summary>
    /// 最多返回条数
    /// </summary>
    public const string MaxCount = "MaxCount";

    /// <summary>
    /// 拉取条数
    /// </summary>
    public const string Count = "Count";

    /// <summary>
    /// 信号名列表（数据曲线、实时曲线）
    /// </summary>
    public const string Names = "Names";

    #endregion

    #region 账号

    /// <summary>
    /// 登录名
    /// </summary>
    public const string UserName = "UserName";

    /// <summary>
    /// 密码
    /// </summary>
    public const string Password = "Password";

    /// <summary>
    /// 旧密码（改自己的密码）
    /// </summary>
    public const string OldPassword = "OldPassword";

    /// <summary>
    /// 新密码（改自己的密码）
    /// </summary>
    public const string NewPassword = "NewPassword";

    /// <summary>
    /// 是不是闲置自动注销的
    /// </summary>
    public const string IsIdle = "IsIdle";

    /// <summary>
    /// 显示名
    /// </summary>
    public const string DisplayName = "DisplayName";

    /// <summary>
    /// 角色编号
    /// </summary>
    public const string RoleId = "RoleId";

    /// <summary>
    /// 启用还是停用
    /// </summary>
    public const string IsEnabled = "IsEnabled";

    /// <summary>
    /// 一个角色的各项权限
    /// </summary>
    public const string Items = "Items";

    #endregion

    #region 报警、EC、整机

    /// <summary>
    /// 报警码
    /// </summary>
    public const string Code = "Code";

    /// <summary>
    /// EC 的键（元件全路径.参数名）
    /// </summary>
    public const string Key = "Key";

    /// <summary>
    /// 晶圆尺寸（8 或 12）
    /// </summary>
    public const string Size = "Size";

    #endregion

    #region 腔体

    /// <summary>
    /// 工艺配方名
    /// </summary>
    public const string Recipe = "Recipe";

    /// <summary>
    /// 设备全路径（如 Chamber1.Arm1）
    /// </summary>
    public const string Device = "Device";

    /// <summary>
    /// 轴的全路径
    /// </summary>
    public const string Axis = "Axis";

    /// <summary>
    /// 目标位置
    /// </summary>
    public const string Position = "Position";

    /// <summary>
    /// 速度
    /// </summary>
    public const string Speed = "Speed";

    /// <summary>
    /// 步进走多远
    /// </summary>
    public const string Distance = "Distance";

    #endregion

    #region Job

    /// <summary>
    /// LoadPort 模块名
    /// </summary>
    public const string LoadPort = "LoadPort";

    /// <summary>
    /// 要做的槽
    /// </summary>
    public const string Slots = "Slots";

    /// <summary>
    /// 流程配方名
    /// </summary>
    public const string Sequence = "Sequence";

    /// <summary>
    /// 批次号
    /// </summary>
    public const string LotId = "LotId";

    /// <summary>
    /// CJ 或 PJ 的名字
    /// </summary>
    public const string JobId = "JobId";

    /// <summary>
    /// 要关联的 PJ 名字列表
    /// </summary>
    public const string ProcessJobs = "ProcessJobs";

    /// <summary>
    /// 库里 CJ 的行号
    /// </summary>
    public const string ControlJob = "ControlJob";

    /// <summary>
    /// 命令值
    /// </summary>
    public const string Command = "Command";

    /// <summary>
    /// CJ 命令的 Action（E94）
    /// </summary>
    public const string Action = "Action";

    /// <summary>
    /// PJ 名字（出错任务的人工处理）
    /// </summary>
    public const string ProcessJob = "ProcessJob";

    /// <summary>
    /// 这一片的第几个任务（从 0 开始）
    /// </summary>
    public const string Task = "Task";

    #endregion

    #region 配方

    /// <summary>
    /// 打开时读到的版本
    /// </summary>
    public const string Revision = "Revision";

    /// <summary>
    /// 步骤
    /// </summary>
    public const string Steps = "Steps";

    /// <summary>
    /// 站点类型
    /// </summary>
    public const string Type = "Type";

    /// <summary>
    /// 超时时间（秒，按文字传）
    /// </summary>
    public const string Timeout = "Timeout";

    /// <summary>
    /// 配方头里自己加的项（字段名 → 值）
    /// </summary>
    public const string Head = "Head";

    #endregion

    #region 机械手、搬运、晶圆账

    /// <summary>
    /// 站点（示教点表里的模块名）
    /// </summary>
    public const string Station = "Station";

    /// <summary>
    /// 源槽号
    /// </summary>
    public const string SourceSlot = "SourceSlot";

    /// <summary>
    /// 目标站点
    /// </summary>
    public const string Target = "Target";

    /// <summary>
    /// 目标槽号
    /// </summary>
    public const string TargetSlot = "TargetSlot";

    /// <summary>
    /// 机械手模块名
    /// </summary>
    public const string Robot = "Robot";

    /// <summary>
    /// 晶圆的内部标识（Guid）
    /// </summary>
    public const string WaferId = "WaferId";

    /// <summary>
    /// 从哪个位置移走
    /// </summary>
    public const string FromModule = "FromModule";

    /// <summary>
    /// 从哪个槽移走
    /// </summary>
    public const string FromSlot = "FromSlot";

    /// <summary>
    /// 移到哪个位置
    /// </summary>
    public const string ToModule = "ToModule";

    /// <summary>
    /// 移到哪个槽
    /// </summary>
    public const string ToSlot = "ToSlot";

    #endregion
}
