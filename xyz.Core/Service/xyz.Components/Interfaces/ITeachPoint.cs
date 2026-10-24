using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 机械手示教点：这台机械手去一个站点（模块）的站号、伸出方向、伸出距离、取放能用哪几只手
/// </summary>
public interface ITeachPoint
{
    /// <summary>
    /// 站点名：示教点的节点名就是它去的模块名（LoadPort1、Chamber1）
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 组件全路径：机械手按它挂在 Size12 还是 Size8 下面分表
    /// </summary>
    string FullPath { get; }

    /// <summary>
    /// 控制器里的站号：取放指令发的就是它
    /// </summary>
    int Number { get; }

    /// <summary>
    /// 伸出方向：界面画机械手朝哪转
    /// </summary>
    RobotDirection Direction { get; }

    /// <summary>
    /// 伸出距离：界面画手臂伸多长
    /// </summary>
    double Y { get; }

    /// <summary>
    /// 从这个站取片能用的手指号，升序；空 = 所有手指都能用
    /// </summary>
    IReadOnlyList<int> PickArmNumbers { get; }

    /// <summary>
    /// 往这个站放片能用的手指号，升序；空 = 所有手指都能用
    /// </summary>
    IReadOnlyList<int> PlaceArmNumbers { get; }

    /// <summary>
    /// 这只手能不能从这个站取片
    /// </summary>
    bool AllowsPick(int arm);

    /// <summary>
    /// 这只手能不能往这个站放片
    /// </summary>
    bool AllowsPlace(int arm);
}
