using System.Text;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Localization;

/// <summary>
/// 腔体设备动作（ChamberDeviceAction，设备错误码的第二个参数）在当前语言里的叫法：界面上显示动作名一律走这里，中文界面不露英文。
/// </summary>
public static class DeviceActionText
{
    /// <summary>语言包 key 的前缀：device.action.{动作名转小写下划线}，如 ValveOn → device.action.valve_on。</summary>
    private const string KeyPrefix = "device.action.";

    /// <summary>动作名的叫法（Move → "移动" / "Move"）；不是认识的动作、语言包里没配就原样给。</summary>
    public static string Of(string action)
    {
        if (!Enum.TryParse(action, out ChamberDeviceAction _))
        {
            return action;
        }

        var key = new StringBuilder(KeyPrefix);
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
}
