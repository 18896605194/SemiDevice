using System.Text;
using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Drivers.Communication;
using xyz.Drivers.Device;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

// 对准器冒烟：装配（没挂驱动开机就抛、登记晶圆账一个槽、开机就是生产模式、站点类型、只取放）、回零（没连上当场拒、台上没片只回零）、
// 片放上来接着对准（搬运照常收尾不等对准、对准中不让取、对完回待命、缺口转到 EC 角度）、只取走不对、交换后对新片、
// 台上的片没对过不让取（重启、人改账）而回零时接着对一遍、对准失败 / 超时落出错报警作废在途指令且复位回零后再对、
// 片放上来起不了对准（驱动断了）落出错报警、中止顶掉对准不报警、状态不允许、停用。
// 不连设备：通道是假的（每条指令按扫描拍数做完），指令是测试自己的，扫描由测试一拍一拍推。
SmokeIoc.Reset();

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("FAIL: " + message);
    }

    checks++;
}

var ec = new EcComponent();
SmokeIoc.Use<IEc>(ec);
var alarms = new AlarmComponent();
SmokeIoc.Use<IAlarm>(alarms);
var ledger = new LedgerManagerComponent();
SmokeIoc.Use<ILedgerManager>(ledger);
SmokeIoc.Use<IMapper>(new MapperComponent());
ledger.InitComponent();

// 片从料盒来、回料盒去：料盒 25 片都有
ledger.RegisterLoadPort("LP1", 25);
ledger.ApplySlotMap("LP1", Enumerable.Repeat<WaferStatus?>(WaferStatus.Normal, 25).ToList(), "C1");

bool Active(ComponentBase source, string code)
{
    return alarms.ActiveAlarms.Any(alarm => alarm.SourcePath == source.FullPath && alarm.AlarmCode == code);
}

bool RunUntil(SmokeAligner target, Func<bool> done, int maxTicks = 50, int sleepMs = 0)
{
    for (int tick = 0; tick < maxTicks; tick++)
    {
        target.Tick();
        if (done())
        {
            return true;
        }

        if (sleepMs > 0)
        {
            Thread.Sleep(sleepMs);
        }
    }

    return false;
}

// 一轮交互，跟搬运管理一样走：准备一 → 准备二 → 交互中 →（机械手动作做完记账）→ 收尾
bool Round(SmokeAligner target, TransferContext context, Action moved)
{
    if (target.PrepareTransfer(context) is null || target.PrepareTransfer2(context) is null || !target.Transferring(context))
    {
        return false;
    }

    moved();
    return target.TransferComplete(context);
}

int nextSlot = 1;

// 机械手从料盒取一片放上台：返回这片的内部标识
Guid PlaceFromCarrier(SmokeAligner target)
{
    int slot = nextSlot;
    nextSlot++;
    var wafer = ledger.Get("LP1", slot)!;
    bool done = Round(target, TransferContext.ForPlace("Robot1", 1, 1), () => ledger.Move("LP1", slot, target.Name, 1));
    Check(done, $"放片一轮走完（料盒 {slot} 号槽 → {target.Name}）");
    return wafer.Id;
}

// 台上的片拿回料盒（记账），不走交互（只为清台面）
void ClearStage(SmokeAligner target)
{
    var wafer = ledger.Get(target.Name, 1);
    if (wafer is not null)
    {
        Check(ledger.Move(target.Name, 1, "LP1", wafer.SourceSlot), "清台面：片回料盒原槽");
    }
}

// 1. 装配：没挂驱动开机就抛；挂了的登记晶圆账一个槽、开机就是生产模式（没有手动页，跟 buffer 一样）、还没回零
{
    var bare = new SmokeAligner("AL0", withDriver: false);
    bool threw = false;
    try
    {
        bare.InitComponent();
    }
    catch (InvalidOperationException)
    {
        threw = true;
    }

    Check(threw, "sc 里对准器下面没挂驱动：组件初始化就抛");
}

var aligner = new SmokeAligner("AL1");
Check(aligner.InitComponent() && aligner._driver is FakeAlignerDriver, "对准器组件初始化：认下面挂的通道");
var driver = (FakeAlignerDriver)aligner._driver!;
Check(driver.Codec is SmokeAlignerCodec, "开机把对准器自己的解码器交给通道");
SmokeIoc.Tree(aligner);
Check(ledger.GetSlots("AL1").Count == 1 && aligner.SlotCount == 1, "晶圆账登记一个槽");
Check(aligner.Mode == ModuleMode.Production && aligner.State == ModuleState.NotInit, "开机就是生产模式、还没回零");
Check(aligner.Kind == StationKind.Aligner && IocHelper.GetServices<IAligner>().Single() == aligner
      && IocHelper.GetServices<ITransferStation>().Single() == aligner,
    "站点类型是对准器；按 IAligner、ITransferStation 都找得到");
Check(aligner.SupportedTasks.SequenceEqual(StationTaskAction.PickPlace), "只声明取放：流程配方这一步不选配方，对准是站点自己的事");
Check(aligner.NotchAngle == 0 && aligner.HomeTimeout == 30000 && aligner.AlignTimeout == 30000
      && aligner.ResetTimeout == 10000 && aligner.AbortTimeout == 10000,
    "EC 默认值：缺口角度 0、回零 / 对准超时 30 s、复位 / 中止超时 10 s");
Check(!aligner.CanPrepareTransfer, "没回零不让机械手来");

// 2. 回零：驱动没连上当场拒（带原因），连上了台上没片只发回零
driver.IsConnected = false;
var refused = aligner.Home();
Check(!refused.IsSuccess && refused.ErrorMessage == ErrorCodes.ModuleNotConnected && refused.Args.SequenceEqual(["AL1"])
      && aligner.State == ModuleState.NotInit && driver.Sent.Count == 0,
    "驱动没连上：回零当场拒 module.not_connected，不发指令");
Check(aligner.InitModule() is null, "全部初始化调到它：回零被拒返回 null");
driver.IsConnected = true;
var home = aligner.Home();
Check(home.IsSuccess && aligner.State == AlignerState.Homing && driver.Sent.Count == 0, "回零挂上了，指令在动作第一拍才发");
aligner.Tick();
Check(driver.Sent.SequenceEqual(["Home"]), "台上没片：只发回零");
var busy = aligner.Home();
Check(!busy.IsSuccess && busy.ErrorMessage == ErrorCodes.ModuleStateNotAllowed
      && aligner.CheckReset()?.ErrorMessage == ErrorCodes.ModuleStateNotAllowed,
    "回零中再回零、复位：状态不允许");
Check(RunUntil(aligner, () => aligner.State == ModuleState.Idle) && !aligner.IsWaferAligned && aligner.CanPrepareTransfer,
    "回零做完回待命，机械手可以来放片");

// 3. 片放上来接着对准：搬运照常收尾（机械手不等对准），对准中不让取，对完回待命；缺口转到 EC 的角度
aligner.NotchAngle = 90;
var first = PlaceFromCarrier(aligner);
Check(aligner.State == AlignerState.Aligning && ledger.Get("AL1", 1)!.Id == first,
    "放片一轮收尾时（搬运照常收尾，不等对准）接着起对准");
aligner.Tick();
Check(driver.Sent.Last() == "Align" && driver.LastAngle == 90, "对准指令带 EC NotchAngle（90°）");
Check(!aligner.CanPrepareTransfer && aligner.CheckPrepareTransfer()?.ErrorMessage == ErrorCodes.ModuleStateNotAllowed,
    "对准中不让机械手来取");
Check(RunUntil(aligner, () => aligner.State == ModuleState.Idle) && aligner.IsWaferAligned && aligner.CanPrepareTransfer,
    "对完回待命、记成对好了，调度这时才来取");

// 4. 只取走：照常回待命、不发对准；台上没片了
int sent = driver.Sent.Count;
Check(Round(aligner, TransferContext.ForPick("Robot1", 1, 1), () => ledger.Move("AL1", 1, "LP1", 1)), "取片一轮走完");
Check(aligner.State == ModuleState.Idle && driver.Sent.Count == sent && !aligner.IsWaferAligned && aligner.CanPrepareTransfer,
    "只取走片：直接回待命，不对准");

// 5. 交换：取走对好的旧片、放下新片（同一轮），新片接着对
var old = PlaceFromCarrier(aligner);
Check(RunUntil(aligner, () => aligner.State == ModuleState.Idle) && aligner.IsWaferAligned, "交换前台上的片对好了");
var swap = TransferContext.ForPickThenPlace("Robot1", 1, 1, 2, 1);
int swapSlot = nextSlot;
nextSlot++;
var fresh = ledger.Get("LP1", swapSlot)!.Id;
bool swapped = aligner.PrepareTransfer(swap) is not null && aligner.PrepareTransfer2(swap) is not null && aligner.Transferring(swap)
    && ledger.Move("AL1", 1, "LP1", ledger.FindById(old)!.SourceSlot)
    && aligner.PrepareNextLeg(swap) is not null && aligner.Transferring(swap)
    && ledger.Move("LP1", swapSlot, "AL1", 1)
    && aligner.TransferComplete(swap);
Check(swapped && aligner.State == AlignerState.Aligning && !aligner.IsWaferAligned, "交换收尾：台上换成新片，接着对新片");
Check(RunUntil(aligner, () => aligner.State == ModuleState.Idle) && aligner.IsWaferAligned && ledger.Get("AL1", 1)!.Id == fresh,
    "新片对好了");

// 6. 台上的片没对过（重启过、人改过账）不让取；回零时台上有片接着对一遍，对完才让取
ClearStage(aligner);
int manualSlot = nextSlot;
nextSlot++;
Check(ledger.Move("LP1", manualSlot, "AL1", 1), "人改账：一片直接记到台上");
var notAligned = aligner.CheckPrepareTransfer();
Check(aligner.State == ModuleState.Idle && notAligned is not null && notAligned.ErrorMessage == ErrorCodes.AlignerWaferNotAligned
      && notAligned.Args.SequenceEqual(["AL1"]) && !aligner.CanPrepareTransfer,
    "待命但台上的片没对过：不让机械手来，回 aligner.wafer_not_aligned");
sent = driver.Sent.Count;
Check(aligner.Home().IsSuccess && RunUntil(aligner, () => aligner.State == ModuleState.Idle)
      && driver.Sent.Skip(sent).SequenceEqual(["Home", "Align"]) && aligner.IsWaferAligned && aligner.CanPrepareTransfer,
    "回零时台上有片：回零后接着对一遍，对完才让取");

// 7. 对准失败：落出错、报警、作废在途指令，片没对好；复位落未初始化，回零（接着对）以后才让取
ClearStage(aligner);
driver.FailNext = "Align";
int abandoned = driver.Abandoned;
var failed = PlaceFromCarrier(aligner);
var alignFailed = aligner.CurrentOperation;
Check(RunUntil(aligner, () => aligner.State == ModuleState.Error) && alignFailed is not null && alignFailed.Code == ErrorCodes.DeviceFailed,
    "对准设备报错：动作失败（device_failed），对准器落出错");
Check(Active(aligner, aligner.ControlledStopAlarm) && driver.Abandoned == abandoned + 1 && !aligner.IsWaferAligned
      && aligner.CheckPrepareTransfer()?.ErrorMessage == ErrorCodes.ModuleStateNotAllowed,
    "报对准器动作失败、作废在途指令，片没对好、不让取");
var reset = aligner.Reset();
Check(reset is not null && RunUntil(aligner, () => reset.IsSettled) && aligner.State == ModuleState.NotInit
      && !Active(aligner, aligner.ControlledStopAlarm) && !aligner.CanPrepareTransfer,
    "复位：清报警、清设备报错，落未初始化（台上片的方向不知道）");
Check(aligner.Home().IsSuccess && RunUntil(aligner, () => aligner.State == ModuleState.Idle) && aligner.IsWaferAligned
      && ledger.Get("AL1", 1)!.Id == failed,
    "回零接着把台上那片对好，才让取");

// 8. 对准超时：按 EC AlignTimeout 判失败，跟设备报错一样收场
ClearStage(aligner);
aligner.AlignTimeout = 1000;
driver.Hang = true;
PlaceFromCarrier(aligner);
var timedOut = aligner.CurrentOperation;
Check(RunUntil(aligner, () => aligner.State == ModuleState.Error, 100, 50) && timedOut is not null && timedOut.Code == ErrorCodes.Timeout
      && Active(aligner, aligner.ControlledStopAlarm),
    "对准等不到回复：超过 EC AlignTimeout 判超时、落出错、报警");
driver.Hang = false;
aligner.AlignTimeout = 30000;
reset = aligner.Reset();
Check(reset is not null && RunUntil(aligner, () => reset.IsSettled) && aligner.Home().IsSuccess
      && RunUntil(aligner, () => aligner.State == ModuleState.Idle) && aligner.IsWaferAligned,
    "复位、回零以后对好");

// 9. 片放上来却起不了对准（驱动断了）：搬运照常收尾，对准器落出错、报警，不发指令
ClearStage(aligner);
driver.IsConnected = false;
sent = driver.Sent.Count;
PlaceFromCarrier(aligner);
Check(aligner.State == ModuleState.Error && Active(aligner, aligner.ControlledStopAlarm) && driver.Sent.Count == sent
      && !aligner.IsWaferAligned,
    "起不了对准：落出错、报警，不发指令，片留在台上没对过");
driver.IsConnected = true;
reset = aligner.Reset();
Check(reset is not null && RunUntil(aligner, () => reset.IsSettled) && aligner.Home().IsSuccess
      && RunUntil(aligner, () => aligner.State == ModuleState.Idle) && aligner.IsWaferAligned,
    "连上、复位、回零以后对好");

// 10. 中止顶掉对准：人按的急停不报警；片的方向不知道，落未初始化
ClearStage(aligner);
driver.Ticks = 20;
PlaceFromCarrier(aligner);
var aborted = aligner.CurrentOperation;
var abort = aligner.Abort();
Check(abort is not null && aborted is not null && aborted.Code == ErrorCodes.Aborted && aligner.State == ModuleState.Aborting,
    "中止顶掉在途的对准");
driver.Ticks = 2;
Check(RunUntil(aligner, () => abort!.IsSettled) && aligner.State == ModuleState.NotInit && driver.Sent.Last() == "Stop"
      && !Active(aligner, aligner.ControlledStopAlarm) && !aligner.IsWaferAligned,
    "发停止，停下落未初始化，人按的急停不报警，片没对好");
Check(aligner.Home().IsSuccess && RunUntil(aligner, () => aligner.State == ModuleState.Idle) && aligner.IsWaferAligned,
    "回零接着对好");

// 11. 停用：不认驱动、不登记晶圆账、不切生产模式，动作一律拒 module.disabled
{
    var disabled = new SmokeAligner("AL2", withDriver: false) { IsEnable = false };
    Check(disabled.InitComponent() && ledger.Locations.All(location => location.Module != "AL2") && disabled.Mode == ModuleMode.Maintenance,
        "停用的对准器：不挂驱动也不抛，不登记晶圆账、不切生产模式");
    var disabledHome = disabled.Home();
    Check(!disabledHome.IsSuccess && disabledHome.ErrorMessage == ErrorCodes.ModuleDisabled && disabled.CheckAbort()?.ErrorMessage == ErrorCodes.ModuleDisabled,
        "停用的对准器：回零、中止都拒 module.disabled");
}

Console.WriteLine($"PASS: {checks} aligner checks (assembly, home, align after place, pick, swap, unaligned wafer, failure, timeout, cannot start, abort, disabled)");
return 0;

// 测试用的对准器：扫描由测试推，每个动作发测试自己的指令
sealed class SmokeAligner : BaseAlignerModule
{
    public SmokeAligner(string name, bool withDriver = true)
    {
        SetName(this, name, name);
        if (withDriver)
        {
            var driver = new FakeAlignerDriver();
            SetName(driver, "Driver", name + ".Driver");
            AddChild(driver);
        }
    }

    public void Tick()
    {
        OnScan();
    }

    protected override IFrameCodec CreateCodec()
    {
        return new SmokeAlignerCodec();
    }

    protected override CommandOperation AddHomeSteps(CommandOperation operation)
    {
        return operation.Then("回零", () => new SmokeAlignerCommand("Home"));
    }

    protected override CommandOperation AddAlignSteps(CommandOperation operation, double angle)
    {
        return operation.Then("对准", () => new SmokeAlignerCommand("Align", angle));
    }

    protected override ModuleOperation CreateResetOperation()
    {
        return new CommandOperation("Reset", _driver, () => new SmokeAlignerCommand("Reset"), () => ResetTimeout);
    }

    protected override ModuleOperation CreateAbortOperation()
    {
        return new CommandOperation("Abort", _driver, () => new SmokeAlignerCommand("Stop"), () => AbortTimeout);
    }

    private static void SetName(ComponentBase component, string name, string path)
    {
        typeof(ComponentBase).GetProperty(nameof(ComponentBase.Name))!.SetValue(component, name);
        typeof(ComponentBase).GetProperty(nameof(ComponentBase.FullPath))!.SetValue(component, path);
    }
}

// 测试的指令：名字就是在途键，回复只带成没成
sealed class SmokeAlignerCommand(string name, double angle = 0) : DeviceCommand<SmokeAlignerReply>
{
    public string Name { get; } = name;

    public double Angle { get; } = angle;

    public override string Key => Name;

    public override string BuildFrame()
    {
        return Name;
    }

    protected override bool Handle(SmokeAlignerReply message)
    {
        if (message.IsSuccess)
        {
            Complete();
        }
        else
        {
            Fail(message.Error);
        }

        return true;
    }
}

sealed record SmokeAlignerReply(bool IsSuccess, string Error);

// 测试的解码器：假通道不收发字节，只为开机交得出去
sealed class SmokeAlignerCodec : IFrameCodec
{
    public Encoding Encoding => Encoding.ASCII;

    public string Wrap(string body)
    {
        return body;
    }

    public IEnumerable<string> Extract(string chunk)
    {
        return [chunk];
    }

    public object? Decode(string frame)
    {
        return frame;
    }
}

// 假通道：每条指令过 Ticks 拍做完；FailNext 摆下一条哪种指令报错，Hang 开着一直不回
sealed class FakeAlignerDriver : ComponentBase, IDeviceDriverComponent
{
    private readonly List<Pending> _inflight = [];

    public bool IsConnected { get; set; } = true;

    public int Ticks { get; set; } = 2;

    public bool Hang { get; set; }

    public string FailNext { get; set; } = string.Empty;

    public int Abandoned { get; private set; }

    public List<string> Sent { get; } = [];

    public double LastAngle { get; private set; }

    public IFrameCodec? Codec { get; private set; }

    public event Action<object>? Reported;

    public void UseCodec(IFrameCodec codec)
    {
        Codec = codec;
    }

    public bool Send(DeviceCommand command)
    {
        if (!IsConnected || command is not SmokeAlignerCommand smoke)
        {
            return false;
        }

        bool fail = FailNext == smoke.Name;
        if (fail)
        {
            FailNext = string.Empty;
        }

        Sent.Add(smoke.Name);
        if (smoke.Name == "Align")
        {
            LastAngle = smoke.Angle;
        }

        _inflight.Add(new Pending(smoke, Ticks, fail));
        return true;
    }

    public void Abandon(DeviceCommand command, string reason)
    {
        for (int index = _inflight.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(_inflight[index].Command, command))
            {
                _inflight[index].Command.Accept(new SmokeAlignerReply(false, reason));
                _inflight.RemoveAt(index);
            }
        }
    }

    public void AbandonAll(string reason)
    {
        Abandoned++;
        foreach (var pending in _inflight)
        {
            pending.Command.Accept(new SmokeAlignerReply(false, reason));
        }

        _inflight.Clear();
    }

    public void Close()
    {
        AbandonAll("PortClosed");
    }

    protected override void OnScan()
    {
        base.OnScan();
        if (Hang)
        {
            return;
        }

        for (int index = _inflight.Count - 1; index >= 0; index--)
        {
            var pending = _inflight[index];
            pending.Left--;
            if (pending.Left > 0)
            {
                continue;
            }

            if (pending.Fail)
            {
                pending.Command.Accept(new SmokeAlignerReply(false, "设备报错"));
            }
            else
            {
                pending.Command.Accept(new SmokeAlignerReply(true, string.Empty));
            }

            _inflight.RemoveAt(index);
        }
    }

    private sealed class Pending(SmokeAlignerCommand command, int left, bool fail)
    {
        public SmokeAlignerCommand Command { get; } = command;

        public int Left { get; set; } = left;

        public bool Fail { get; } = fail;
    }
}
