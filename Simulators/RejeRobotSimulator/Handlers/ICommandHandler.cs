using RejeRobotSimulator.Protocol;

namespace RejeRobotSimulator.Handlers;

/// <summary>
/// 指令处理器公共接口：输入解析后的指令，输出第二次回复帧（可多帧，用 '\n' 分隔）。
/// </summary>
public interface ICommandHandler
{
    string Handle(ParsedCommand command);
}
