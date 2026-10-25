using SqlSugar;

namespace xyz.Database.Eda;

/// <summary>
/// EDA（Interface A）要掉电保持的设定，一块一行、整块存成 JSON：1 号行是权限表和最大会话数（E132），2 号行是持久的采集计划（E134）
/// </summary>
[SugarTable("eda_config")]
public class EdaConfigEntity
{
    /// <summary>
    /// 哪一块（1 权限、2 采集计划）
    /// </summary>
    [SugarColumn(IsPrimaryKey = true)]
    public int Id { get; set; }

    /// <summary>
    /// 这一块的内容（JSON）
    /// </summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string Json { get; set; } = string.Empty;

    /// <summary>
    /// 存的时刻
    /// </summary>
    public DateTime SavedAt { get; set; }
}
