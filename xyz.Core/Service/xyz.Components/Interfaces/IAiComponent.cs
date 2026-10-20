using xyz.Components.Models;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// AI 助手：只读报警、日志、信号，给建议，不动设备
/// </summary>
public interface IAiComponent
{
    /// <summary>
    /// 模型名（sc 里配的）
    /// </summary>
    string Model { get; }

    /// <summary>
    /// 解释一条当前报警：可能的原因、先查哪里；language 是界面语言（zh-CN / en-US），回答用它
    /// </summary>
    Task<HandleResult<AiAnswer>> ExplainAlarmAsync(string sourcePath, string alarmCode, string language, CancellationToken cancellationToken);
}
