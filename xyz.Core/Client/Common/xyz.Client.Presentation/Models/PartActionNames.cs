using System.Text;
using xyz.Client.Presentation.Localization;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 部件手动动作名：就是后端组件上标了 [ManualAction] 的方法名，改了方法名这里跟着改。
/// 界面上显示动作名（比如后端报错里带的）一律走 <see cref="LabelOf"/> 换成语言包里的叫法，不直接露方法名。
/// </summary>
public static class PartActionNames
{
    /// <summary>动作名的语言包 key 前缀：part.action.{方法名转小写下划线}，如 MoveTo → part.action.move_to。</summary>
    private const string LabelKeyPrefix = "part.action.";

    /// <summary>
    /// 动作名在当前语言里的叫法（方法名 MoveTo → 语言包 part.action.move_to → "移动" / "Move"）；语言包里没配就原样给方法名。
    /// </summary>
    public static string LabelOf(string action)
    {
        var key = new StringBuilder(LabelKeyPrefix);
        for (int i = 0; i < action.Length; i++)
        {
            char letter = action[i];
            if (char.IsUpper(letter) && i > 0)
            {
                key.Append('_');
            }

            key.Append(char.ToLowerInvariant(letter));
        }

        return L10n.Find(key.ToString()) ?? action;
    }

    /// <summary>轴回零。</summary>
    public const string Home = "Home";

    /// <summary>轴走到绝对位置：参数 [位置, 速度（可省）]。</summary>
    public const string MoveTo = "MoveTo";

    /// <summary>轴走一段（步进）：参数 [位移（正负是方向）, 速度（可省）]。</summary>
    public const string MoveBy = "MoveBy";

    /// <summary>轴点动（按住类）：参数 [速度（正负是方向）]；按住期间续，松手发 Stop。</summary>
    public const string Jog = "Jog";

    /// <summary>轴停止（停止类，腔体忙也照发）。</summary>
    public const string Stop = "Stop";

    /// <summary>轴驱动器复位清错。</summary>
    public const string ResetDrive = "ResetDrive";

    /// <summary>双作用气缸到开侧（门开、Bowl 升、Lift 升）。</summary>
    public const string Open = "Open";

    /// <summary>双作用气缸到关侧（门关、Bowl 降、Lift 降）。</summary>
    public const string Close = "Close";
}
