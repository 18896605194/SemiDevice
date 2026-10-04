using System;

namespace xyz.Components.Attributes;

/// <summary>
/// 手动页显示的实时数据：标在组件的公开属性上。模块按 sc.xml 的组件树把标了 <see cref="PartKindAttribute"/> 的组件扫出来，
/// 每个扫描周期读一遍这些属性，有变化就推给界面（腔体手动页的轴页签、气缸表、三维图都从这里取数）。
/// 跟 SV 不是一回事：SV 是给 EAP 采集的，这个只管界面显示，两个可以标在同一个属性上。
/// 推的是字符串：数字按不变区域性，浮点按 Decimals 位四舍五入（编码器在这一位以下抖不会一直推），布尔 True / False，枚举写名字。
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class LiveValueAttribute : Attribute
{
    /// <summary>
    /// 浮点数保留几位小数，默认 3（界面上位置、速度都显示 3 位）；整数、布尔、枚举不看它。
    /// </summary>
    public int Decimals { get; set; } = 3;
}
