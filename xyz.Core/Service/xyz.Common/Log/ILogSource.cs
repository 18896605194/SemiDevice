namespace xyz.Common.Log;

/// <summary>
/// 写日志的来源
/// </summary>
public interface ILogSource
{
    /// <summary>
    /// 第一层：machine.xml 树上最顶上那个元件的全路径（带外面的分组名），比如 LoadPort.LoadPort1
    /// </summary>
    string LogModule { get; }

    /// <summary>
    /// 第二层：从第一层往下剩的路径，比如 Serial、RFID.Serial；模块自己写的为空
    /// </summary>
    string LogPart { get; }
}
