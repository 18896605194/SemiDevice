using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

namespace xyz.Client.Manual.Models;

/// <summary>
/// 腔体显示模型，腔体手动页直接绑它；只做显示，启用、模式、设备报错、当前配方、片位（晶圆账）都由后端按 sc.xml 与晶圆账推过来。
/// 状态推送来了就地刷新；片位没变就不换圆片列表，俯视图不会跟着别的字段一起重建。
/// </summary>
public class ChamberModel : ObservableObject
{
    private string _name = string.Empty;

    /// <summary>模块实例名，与 EventBus token 一致，如 "Chamber1"。</summary>
    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    private int _state;

    /// <summary>模块状态码，取值见 ModuleState/TransferModuleState/ChamberState。</summary>
    public int State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateText));
            }
        }
    }

    private ModuleMode _mode;

    /// <summary>模块模式（Online=参与自动调度 / Offline）。</summary>
    public ModuleMode Mode
    {
        get => _mode;
        private set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsOnline));
            }
        }
    }

    /// <summary>模式灯：Online 亮。</summary>
    public bool IsOnline => Mode == ModuleMode.Online;

    private bool _isEnable;

    /// <summary>是否启用（sc.xml 腔体节点的 IsEnable）：停用时动作一律发不出去。</summary>
    public bool IsEnable
    {
        get => _isEnable;
        private set
        {
            if (SetProperty(ref _isEnable, value))
            {
                OnPropertyChanged(nameof(HasDeviceError));
            }
        }
    }

    private string? _deviceError;

    /// <summary>设备当前报错；null 表示无报错或腔体停用。</summary>
    public string? DeviceError
    {
        get => _deviceError;
        private set
        {
            if (SetProperty(ref _deviceError, value))
            {
                OnPropertyChanged(nameof(HasDeviceError));
            }
        }
    }

    private string? _recipe;

    /// <summary>当前配方：最近一次发起成功的工艺配方名；还没做过工艺为 null。</summary>
    public string? Recipe
    {
        get => _recipe;
        private set
        {
            if (SetProperty(ref _recipe, value))
            {
                OnPropertyChanged(nameof(RecipeText));
            }
        }
    }

    private int _slotCount;

    /// <summary>片位数（sc.xml 腔体节点的 SlotCount）；还没收到状态时为 0，俯视图不画片位。</summary>
    public int SlotCount
    {
        get => _slotCount;
        private set => SetProperty(ref _slotCount, value);
    }

    private IReadOnlyList<WaferModel> _wafers = [];

    /// <summary>
    /// 腔里的片：有片的片位各给一片，交给腔体俯视图按片位号摆；颜色按工艺状态（未做 / 工艺中 / 做完 / 失败），中间写片号。
    /// </summary>
    public IReadOnlyList<WaferModel> Wafers
    {
        get => _wafers;
        private set => SetProperty(ref _wafers, value);
    }

    private List<ChamberSlotDto> _slots = [];

    /// <summary>
    /// 状态文字（按当前语言）。码值对应 xyz.Modules 的 ModuleState/TransferModuleState/ChamberState，未收录的码显示原值。
    /// </summary>
    public string StateText
    {
        get
        {
            switch (State)
            {
                case 10: return L10n.Get("module.state.not_init");
                case 20: return L10n.Get("module.state.initing");
                case 30: return L10n.Get("module.state.idle");
                case 35: return L10n.Get("module.state.aborting");
                case 40: return L10n.Get("module.state.error");
                case 50: return L10n.Get("module.state.pre_transfer");
                case 60: return L10n.Get("module.state.transfer_ready");
                case 70: return L10n.Get("module.state.transferring");
                case 80: return L10n.Get("module.state.transfer_complete");
                case 100: return L10n.Get("module.state.homing");
                case 110: return L10n.Get("module.state.processing");
                default: return L10n.Get("module.state.unknown", State);
            }
        }
    }

    /// <summary>设备报错灯：腔体启用且报错有内容才亮。</summary>
    public bool HasDeviceError
    {
        get
        {
            if (!IsEnable)
            {
                return false;
            }

            return !string.IsNullOrEmpty(DeviceError);
        }
    }

    /// <summary>当前配方文字；还没做过工艺显示占位符。</summary>
    public string RecipeText
    {
        get
        {
            if (string.IsNullOrEmpty(Recipe))
            {
                return L10n.Get("chambermanual.na");
            }

            return Recipe;
        }
    }

    /// <summary>
    /// 用推送的状态就地刷新（界面线程调用）。
    /// </summary>
    public void Update(ChamberDto dto)
    {
        Name = dto.Name;
        State = dto.State;
        Mode = dto.Mode;
        IsEnable = dto.IsEnable;
        DeviceError = dto.DeviceError;
        Recipe = dto.Recipe;
        SlotCount = dto.SlotCount;

        // 片位跟上次一样就不换圆片列表：每次推送都换，俯视图会跟着模式、报错这些字段一起重建。
        if (!SameSlots(dto.Slots, _slots))
        {
            _slots = dto.Slots;
            Wafers = dto.Slots
                .Where(slot => slot.HasWafer)
                .Select(ToWafer)
                .ToList();
        }
    }

    private static bool SameSlots(IReadOnlyList<ChamberSlotDto> left, IReadOnlyList<ChamberSlotDto> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (left[i].Slot != right[i].Slot
                || left[i].State != right[i].State
                || left[i].WaferId != right[i].WaferId)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 片位上的片转圆片：颜色沿用圆片控件的状态色——未做 = 待加工、工艺中、做完、失败 / 中止 = 报错。
    /// </summary>
    private static WaferModel ToWafer(ChamberSlotDto slot)
    {
        string state;
        switch (slot.State)
        {
            case ChamberSlotState.InProcess:
                state = "Process";
                break;
            case ChamberSlotState.Completed:
                state = "Completed";
                break;
            case ChamberSlotState.Failed:
            case ChamberSlotState.Aborted:
                state = "Error";
                break;
            default:
                state = "IdleHasjob";
                break;
        }

        return new WaferModel
        {
            Slot = slot.Slot,
            LpSlot = slot.WaferId ?? string.Empty,
            State = state,
        };
    }
}
