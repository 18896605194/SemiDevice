namespace xyz.Components.DataCharts;

/// <summary>
/// 数据曲线采的一个信号：名字就是库表里的列名，也是界面勾选树的路径（按点号分层），全部由 sc.xml 生成——
/// SV = 组件全路径.属性名；组件绑的 IO = 组件全路径.配置项名去掉 Index（sc.xml 里 Door 配了 DiOpenedIndex，列名就是 Chamber1.Door.DiOpened）。
/// 点表只提供这个点的地址、单位、说明，不决定名字；接线改了点号，名字不变，历史接得上。
/// </summary>
/// <param name="Name">信号名（列名）。</param>
/// <param name="IsDigital">开关量（DI/DO、布尔 SV）：库里存 1/0（true=1、false=0），界面按开关量分道画。</param>
/// <param name="Source">来源：SV / DI / DO / AI / AO。</param>
/// <param name="Address">IO 点的地址（DI100 这种）；SV 为空。</param>
/// <param name="Unit">工程单位，没有为空。</param>
/// <param name="Description">说明（SV 的描述；IO 是点表里的点名和描述）。</param>
/// <param name="Read">现读当前值；读不到（PLC 断线、点位无效）给 null，库里存空，不存 0。</param>
public sealed record DataSignal(
    string Name,
    bool IsDigital,
    string Source,
    string Address,
    string Unit,
    string Description,
    Func<double?> Read);

/// <summary>
/// 一个采样周期的一行：Time 是 UTC 毫秒（按采样周期对齐），Values 跟信号表一一对应，读不到的是 null。
/// </summary>
public sealed record DataRecord(long Time, double?[] Values);
