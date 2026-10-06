# 后端

路径相对 `xyz.Core`。.NET 10（`net10.0`），Nullable、ImplicitUsings 全开；`D:\Code\Directory.Build.props` 统一 win-x64、x64。

## 1. 工程和依赖方向

```
Shared\xyz.Tools      EventBus、JsonHelper、XmlHelper、IocHelper（不引用任何工程）
Shared\xyz.Shared     契约：Services\I*Service、Dtos\、Errors\ErrorCodes、Rpc\（→ Tools）
Service\xyz.Common    日志：LogHelper、LogQueue、LogFileReader、NLog.config
Service\xyz.Configs   SC（读 sc.xml）、配置模型、Config\sc.xml、Paths.json（→ Common、Tools）
Service\xyz.Database  SqlSugar：XyzDb、实体、分表
Service\xyz.Drivers   纯协议/通讯（不引用 Components）
Service\xyz.Secs      SECS-II / HSMS（零依赖）
Service\xyz.Components 组件 + 模块动作 ModuleOperation + 设备侧给 EAP 的接口（→ Configs、Database、Drivers、Secs、Shared）
Service\xyz.Modules   模块（→ Components、Drivers、Shared）
Service\xyz.Service   gRPC 服务实现 + 装配 + 事件桥（→ Shared、Tools、Database、Modules、Configs）
Service\xyz.GrpcHost  宿主（WinExe，托盘图标，单实例；→ Shared、Service）
```

- 组件层引用 xyz.Shared 只为错误码和 EAP 接口用到的 Job DTO（2026-10-05 起；原来不引用）。"组件事件 → EventBus 推客户端"的桥照旧都搭在
  `xyz.Service\ServiceExtensions.cs`，组件里不直接推客户端。组件层的内部成员只对 xyz.Modules 开放（`InternalsVisibleTo`）：模块基类要控制
  ModuleOperation"什么时候算真正做完"（`DeferCompletion` / `NotifyCompletion`）；所以 xyz.Modules 里重写 `protected internal` 的成员
  （`OnSettingLoaded`）要写 `protected internal override`，机型工程照旧 `protected override`。
- 平台不引用机型工程；机型 DLL 由 DeployToHost 拷到宿主 `Modules\<机型>\`，装配时按目录扫描。
- 没有单元测试工程，测试是 `D:\Code\tools\*Smoke` 控制台程序（见 machine-and-tools.md）。

## 2. 组件（`xyz.Components\ComponentBase.cs`）

- 属性：`Name`、`FullPath`（"Chamber1.Door"，纯分组节点不进路径）、`InitOrder`（默认 10000）、`Children` / `AddChild`；
  查找 `FindChild(name)`、`FindChild<T>(name)`、`FindChild<T>()`、`FindChildren<T>()`。
- 扫描：根组件（模块、PLC、Safety、TransferManager、JobManager）调 `Start()` 起一条长任务循环：`OnScan()` → 慢扫描检查 → `Thread.Sleep(50)`。
  `protected virtual void OnScan()` 会递归子组件，重写时先调 `base.OnScan()`。
- 生命周期：`Init()`（先子后己、按 InitOrder，开机不自动调）、`Abort()`（只停，不清报警）、`Reset()`（先子，再清本组件报警）。
  模块把返回类型收窄成 `ModuleOperation?`。`Open()` 不在基类，各类型自己定义（模块、PLC、IO、HSMS、驱动、轴）。
- 配置钩子：`OnSettingLoaded(ModuleConfig)`——[SCEditor] 灌完值后调，配置不对就抛异常（开机直接报出来）。
- 单例：`public static X? Current { get; set; }` + 构造里 `Current = this;`（报警、EC、System、Log、Rpc、WaferManager、Io、Safety、
  Eap、Hsms、E30、DataChart、RealChart、PLC、TransferManager、JobManager、GemCollectors、SequenceComponent、ProcessRecipeComponent）。用的地方 `X.Current` 先取到变量再判空，没装就降级不崩。

### 装配（`ComponentLoader`）
- 类上 `[Component(description: "中文说明")]`，sc.xml 的 `Type` 写**类型全名**（如 `xyz.Components.Components.CylinderComponent`、
  `xyz._35021.Module.Loadport.LoadPortModule`）——所以组件挪目录不能改命名空间。需要 public 无参构造。
- 扫运行目录 `*.dll` + `Modules\**\*.dll`；找不到 Type 直接抛异常（宿主托盘变红）。
- 没写 Type 的节点：父组件已有同名子组件就灌值给它，否则是纯分组（LoadPort、Database、Chamber），名字不进 FullPath。
- 灌值：先把所有可写 [SCEditor] 属性设成默认值，再按 `<Value Name>`（不分大小写）覆盖；支持 int / double / bool / string / enum（InvariantCulture），转不了抛异常带节点名和值。

### 参数：SC 和 EC（代码里不留魔法数）
- **SC**（装机、接线、结构性的，改了重启生效）：`[SCEditor("默认值", "分组", "说明")]`，C# 初始值和默认值写成一样：
  ```csharp
  [SCEditor("25", "LoadPort", "花篮槽数")]
  public int SlotCount { get; set; } = 25;
  ```
  IO 下标默认 `"-1"`（没接）。开关一律 bool（`IsEnable`），不用 High/Low 这类字符串。
- **EC**（现场在线调的：超时、防抖、周期、批量，改了下一拍生效）：
  ```csharp
  [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "10000", "200", "报警防抖时间")]
  public int DebounceMs { get { return GetEcInt(nameof(DebounceMs)); } set { SetEcInt(nameof(DebounceMs), value); } }
  ```
  键是"组件全路径.属性名"。ec.xml 不随源码，后端启动时 `EcComponent.Merge` 按组件树生成（只补新项、不动已有值）；界面在 设置 → EC 设置 改，
  后端校验格式/范围后写回并推送。
- 协议、结构常量用 `const`。新组件先分好 `#region SC` / `#region EC` 再写逻辑。

### 报警
```csharp
[Alarm("开/关到位超时", AlarmCategory.Timeout, AlarmLevel = AlarmLevel.Alarm1, Description = "...", Solution = "...")]
public string TimeoutAlarm = nameof(TimeoutAlarm);
```
- 报：每拍条件型 `CheckAlarm(code, condition, debounceMs)`；一次性 `RaiseAlarm(code)`。不用事先注册，第一次报时反射取定义。
- **报警只能人工复位清**（`AlarmComponent.Reset(sourcePath)` → 组件 `Reset()`），源头恢复、动作成功都不自动清。
- 历史入库（按天分表），报出、清除各一行。客户端推送由 Service 层桥接 `AlarmChanged`。
- SECS 用的 SV / EC / ALID / CEID / DV 编号表由 `GemCollectors.Merge` 按代码声明生成（`*Definitions.xml`，号段固定、删掉的停用保号）；
  事件声明 `[EventAttribut("FOUP 到达")] public readonly string FoupArrivedEvent = "FoupArrived";`（类名少个 e，是历史拼写），
  事件带的数据（DV）先声明 `[DataVariable(ValueFormat.String, "载具号")] public readonly string CarrierIdData = "CarrierID";`，
  事件上写 `Data = new[] { "CarrierID" }`（同一组件上的 DV 代码）；报事件 `RaiseEvent(FoupArrivedEvent, new GemData("CarrierID", id))`——
  经 E30 统一发 S6F11，值当场取好、排进发送线程，不阻塞（扫描线程上也能调）；没接 EAP、离线、Host 关了这个事件时什么都不做。
  SV 属性可以直接返回 `SecsItem`（格式要精确的，比如列表），也可以是 byte / ushort / uint（报 U1 / U2 / U4），int 照老规矩非负报 U4。

### 目录（xyz.Components）
- 顶层只有 `Attributes` / `Collectors` / `Components` / `Enums` / `Interfaces` / `Models` + `ComponentBase.cs`、`ComponentLoader.cs`。
  顶层目录 = 命名空间（`xyz.Components.Models` 等）。
- `Components\` 下按类别分 System / Plc / Actuators（气缸、阀、喷嘴、灯）/ Sensors / Motion / Drivers（品牌驱动壳）/ Charts / Eap，
  **命名空间一律 `xyz.Components.Components`**（子目录只归类）。
- 纯数据类进 `Models`（一个类一个文件），枚举进 `Enums`；只给某个组件用的内部类跟着组件放。不建按领域分的顶层目录。
- 模块动作 `ModuleOperation`（和泛型版、`NoOpOperation`、等待扩展）在 `Components\Operations`（命名空间 `xyz.Components.Components`），
  `OperationState` 在 `Enums`——2026-10-05 从模块层挪下来，好让设备侧接口放进组件层。
- `Interfaces` 下除了组件自己的（IPlc、IActionComponent……），还有设备侧给 EAP 的命令接口和上报口：`ILoadPort`、`IE87Callback`、`IE84Callback`、
  `IE84Provider`、`IJobManager`、`IE40Callback`、`IE94Callback`、`IE90Callback`（挂在晶圆账 `WaferManager.E90Callback` 上）；它们用到的 `E84Timer`、`LoadPortTransferState`、CJ / PJ 的状态和命令、
  `JobCommandSource` 在 `Enums`，Job 的请求（`ProcessJobSpec`、`ControlJobSpec`，本地、Host 共用）在 `Models`；命令结果用 xyz.Shared 的 `HandleResult`
  （失败时 `ErrorMessage` 放错误码、`Args` 放参数）。
  实现还在模块层（`BaseLoadPortModule`、`JobManager`）；EAP 组件写在组件层，直接用这些接口。

## 3. 模块（`xyz.Modules`）

- `BaseModule`：`abstract int State`（子类加 `[VariableMark(SV, Int, ...)]`，初值 `ModuleState.NotInit`）、`Open()`、
  `Online()/Offline()`、动作迁移表（`(状态, 动作)` → 执行中/成功状态）、`Begin(action, operation)`（不允许就返回 null，只有 Abort 能顶替在途动作）。
- 状态码（`public const int`，有继承）：`ModuleState` NotInit 10 / Initing 20 / Idle 30 / Aborting 35 / Error 40；
  `TransferModuleState` 50/60/70/80；`LoadPortState` 100~150；`ChamberState` Homing 100 / Processing 110 / Manual 120（部件手动动作中）；`RobotState` 200/210/220。
  同一个码在不同模块意思不同，客户端按模块种类翻（`ModuleStates`）。
- **动作 = ModuleOperation**：
  ```csharp
  public override ModuleOperation? Home() => Begin(LoadPortAction.Home, new HomeOperation(this));

  sealed class HomeOperation : ModuleOperation<ActionStep>
  {
      protected override void OnScan()   // 只在扫描线程跑，不加锁
      {
          switch (Step)
          {
              case ActionStep.SendCommand: ... SetStep(ActionStep.WaitCommand); break;
              case ActionStep.WaitCommand:
                  if (完成) Complete();
                  else if (Watch.ElapsedMilliseconds > _module.HomeTimeout) Fail(ErrorCodes.Timeout, "日志用原因", 参数...);
                  break;
          }
      }
  }
  ```
  `Complete()` / `Fail(code, reason, args)` / `AbortByHost(reason)`；`Reason` 只进日志，`Code + ErrorArgs` 给界面；OnScan 抛异常自动 `Fail(OperationFaulted)`。
  RPC 线程用 `WaitReply(ms)` 等结果，**不能在扫描线程等**。超时时间取模块的 EC 属性。
- 动作失败（非 Abort）模块报 `ControlledStopAlarm`；设备报错每拍 `RaiseAlarm(XxxDeviceAlarm)`。
- 站点类：`BaseTransferStationModule`（SlotCount、传片环 PrepareTransfer → Transferring → TransferComplete）、
  `BaseLoadPortModule`（子组件按类型找 Driver / RFID / E84；Open 里先开 RFID、E84，再登记晶圆账槽位、开驱动——RFID、驱动这一次没连上也照样往下走，
  返回 false 只为开机日志看得到，之后由驱动组件按间隔重连；**设备状态查询在平台**：每拍一条 GET:STATE，超过 EC `QueryDataTimeOut` 没回就作废这一条、
  Status 清空、下一拍重发，超时 / 恢复各记一次日志，机型不用写；**在位二选一**（SC `PresenceSource`，默认 Query）：Query 看状态查询的在位、到位两位，
  都亮放好、都灭拿走、一亮一灭或查不到不算变化，Event 看 PODON / PODOF（`NotePodEvent`，机型有别的上报路子也调它），只在扫描线程判边沿，
  判出来的叫 `IsCarrierArrived`（载具到了，推给界面的"在位"也是它；状态查询的原始位叫 `IsPresent` / `IsPlaced`，`LoadPortStatus` 的开关量一律 `Is` 开头）；动作没做成（失败、超时、被顶替）在 `OnOperationCompleted` 里把驱动的在途指令全部作废；**7 个动作平台给默认实现**（`LoadPortCommandOperation`：发驱动指令 → 等完结 → 超时判失败，Load 成功调 `UpdateSlotMap`），每拍最后 `PublishState`，机型类只在动作不一样时重写；
  E84 子组件 SC `IsEnable`=False（本机没接搬运车）时 `E84` 属性为 null，端口当没有 E84：不打开、不每拍推、不读写 IO——
  跟 EC `E84Enabled`（装了以后现场在线开关交接）分开）、
  `BaseRobotModule`（sc.xml 子节点 `Stations` 读站点表：Number、Y、Direction、Arms；推送的站点表还带槽数、站点类型 Kind；`Pick/Place(arm, 站点名, slot)` 成功后改晶圆账）、
  `BaseChamberModule`（Open 只登记晶圆账）。
- **手动部件是通用的**（组件自己声明，模块不认具体硬件）：组件类标 `[PartKind("Axis")]`（派生类继承；现有 `Axis` 轴、`TwoState`
  双作用气缸、`OneState` 阀 / 喷嘴），属性标 `[LiveValue]`（推给界面的实时数据，浮点按 `Decimals` 位取整，默认 3），
  方法标 `[ManualAction]`（返回 bool = 指令发没发出去；`Priority = true` 停止类，`Release = "Stop"` 按住类）。
  自己管动作到完成的组件实现 `IActionComponent`（`ActionState`）。`xyz.Modules\Parts\PartCatalog` 照模块的组件树（先父后子）
  收标了种类的组件，反射结果按类缓存；`CreateDto()` 出 `ModulePartsDto`（每个部件：Path、Kind、Type = 组件类名、Values 字典，
  值都是不变区域性字符串），`Find(路径)` → `ManualPart.TryGetAction(方法名)` → `ManualPartAction.TryBind(参数字符串)` / `Invoke`。
  新硬件要上手动页：类上标种类、属性和方法上标特性，推送、动作接口都不用改；界面按 Kind 选模板（不认识的种类只收数据）。
- 腔体部件（`BaseChamberModule`）：`PublishState()` 里顺带推 `ModulePartsDto`（token 模块名、留存、有变化才推，跟 `ChamberDto` 类型不同互不覆盖）。
  `TryPartAction(路径, 动作名, 参数)` → `ChamberService.PartActionAsync(PartActionRequest)`：普通动作走迁移表 `Manual`
  （未初始化 / 空闲 / 报错可发，执行中 `ChamberState.Manual` 120，做完回原来的状态），指令在锁内发，发不出去回 `chamber.part_command_rejected`
  不改状态、不报警，`ChamberPartOperation` 只看部件的 ActionState（没有就发出去算完），EC `PartActionTimeout` 兜底；
  停止类不看忙不忙、不挂操作，发出去就回；按住类（点动）挂 `ChamberHoldOperation` 等松手——界面按住期间调 `RenewPartActionAsync` 续，
  EC `HoldTimeoutMs`（默认 1000）内没续上就自己发松手动作，松手后等部件停下才退出 Manual。错误码：`chamber.part_not_found`、
  `chamber.part_action_unsupported`（组件上没有这个 [ManualAction] 方法）、`chamber.part_action_args_invalid`、`chamber.part_not_held`。
  气缸 `TwoStateComponent.Position` 三态（命令发到哪侧看哪侧到没到位，没到 = Unknown；两个线圈都没通时只看到位反馈）。
  摆臂 `ArmAxisComponent.Reach`（0 = 回零的 0 位，1 = EC Center）/ `EdgeReach`（Edge / Center，示教过才有，三维分两段画），
  示教位 `Edge` = 配方 0（第一个边缘）、`Center` = 配方 150（晶圆中心）的实际轴位置，默认 0 / 150；旋转电机 `IsSpinning`。
  轴手动页参数默认值 EC：`MoveSpeed`、`JogSpeed`（点动 / 步进速度）、`JogStep`（步距）。
  轴的"目标已在到位容差里就不发"只用于绝对定位（MoveTo）；相对移动（MoveBy，步进）照发——步距默认 1 跟到位容差默认 1 一样大，
  以前会被当成已经到位、根本不走（2026-10-04 联调时发现）。联锁还没做。
- 状态推送：`PublishState()` 里 `EventBus.Send(dto, Name)`（token = 模块名，留存），只在变化时发（`dto.HasStateChanged(上一次)`）。
  能放片的模块在推送里带上晶圆账（`LedgerSlots = WaferLedgerSnapshot.SlotsOf(Name)`，腔体是 `Slots`），并在 HasStateChanged 里比较——
  账一变下一拍就推给所有界面，不另发通知；界面画片以账为准（见 decisions.md）。
- 流程配方库 `Recipe\SequenceComponent`（sc.xml `Sequence` 节点，SC：`Capacity` 99 个编号、`Folder` `Recipe\Sequence`、`NameMaxLength` 32）：
  编号 1~Capacity，一个编号一个文件（`001.xml`，`SequenceData`，先写 `.tmp` 再整个替换）；坏文件、编号越界的文件跳过并记日志。
  可选站点分组由 `Bind(settings, modules)` 生成（模块全起来后调）：sc.xml 顶层没 Type 的分组节点下、机械手 `Stations` 到得了的站点模块，
  顶层直接装的站点自成一组；第 1 步和最后一步必须是 LoadPort 组（组里全是 LoadPort），有腔体的组每步要填工艺配方；名字一律照 sc.xml 原样。
  保存带版本号（对不上回 `sequence.revision_mismatch`，防两个人同时改），至少 3 步；`Changed(编号)` 在锁外发。服务 `ISequenceService`（`xyz.Service\Recipes`）。
- 工艺配方库 `Recipe\ProcessRecipeComponent`（sc.xml `ProcessRecipe` 节点，SC：`Capacity` 99、`Folder` `Recipe\Process`、`NameMaxLength` 32）：
  编号、文件、版本号、`Changed(编号)` 跟流程配方库一样。**每一步有哪些字段不写死**，按本节点下 `Fields` 分组的字段表（一个子节点一个字段 = 配方页一列，
  节点名就是字段名；值：`Text` / `TextEn` 列名、`Type` Int / Double / Choice / Bool / Text、`Unit`、`Min` / `Max`、`Decimals`、`Default`、`Required`、`Source`），
  `OnSettingLoaded` 里读（`ProcessRecipeField.FromConfig`），配错就抛（字段名格式、重复、必须有 `Seconds` 且是 Double、上下限、默认值、数据源写法……）。
  下拉的数据源 `ProcessRecipeSource`：直接写选项（`Time,Scan`），或 `Parts:类型[.属性][@字段名]` 从腔体部件取（类名或基类名认部件，属性反射取值；
  @ 跟的字段要是从部件取名字的下拉，只在它选中的部件下面找）。`Bind(modules)` 按每个腔体取一遍（数据源写的属性部件上没有就抛），
  界面拿所有腔体合起来的（`ChoicesOf`），每个腔体自己的留着给 `FindMismatch(配方, 腔体)`：流程配方保存（`sequence.recipe_option_missing`）、
  腔体起工艺（`chamber.recipe_option_missing`）时查勾的腔体有没有配方里选的值。检查按字段表（`process_recipe.value_*` 一组通用错误码，
  字段名按 System 语言取中文名或英文名），字段之间不互相管；合计时长（`Seconds` 加起来）不超过腔体 EC `ProcessTimeout`（几个腔取最小，每次现查）。至少 1 步，新建时带一步默认值。
  文件：一步一个 `Step`，每个字段写成一个属性（`XmlAnyAttribute`），**字段表里的字段都写、空的也写**——读的时候没有这个属性就是字段表后来加的，
  按默认值补；字段表里没有的属性先留着、下次保存丢掉。存之前规整写法（整数、小数去多余写法，开关小写，下拉按数据源的大小写）。
  按名字被引用：流程配方查配方在不在库里（`sequence.recipe_not_found`），腔体起工艺查（`chamber.recipe_not_found`）；库没装都不查。
  锁的先后：`SequenceComponent` 锁里可以调 `ProcessRecipeComponent.Contains` / `FindMismatch`，反过来不行。服务 `IProcessRecipeService`（`xyz.Service\Recipes`），
  `GetOptionsAsync` 给字段表（带取好的下拉选项，跟着别的字段走的按那个字段的值分开给）。两个库的 `Find(名字)` 给的是克隆（配方快照，库里再改不影响）。
  还没做：腔体按工艺配方的步骤真的去转、去喷（35021 的 Process 还是定时模拟，只认名字）；字段作用到哪个设备（AO 等，本来就配在 sc 腔体下面）到时再定。
- **加工口** `Process\IProcessStation`（`BaseChamberModule` 实现）：手动起工艺和 Job 走同一个口子。`CheckProcess(ProcessRequest)` 只问不动设备
  （没配方 → 配方对不上这个腔体 → 槽号 / 片号对不上 `chamber.wafer_mismatch` → 状态不允许 / 在忙 `module.action_rejected`），
  `StartProcess` 在锁里 Begin，起了把账上的片标成 InProcess，做完（`OnOperationCompleted`）标 Completed / Failed / Aborted。
  机型只写 `CreateProcessOperation(ProcessRequest)`（请求里带配方快照）。
  `ChamberService.ProcessAsync` 先取库里的配方快照，腔里的片归某个 Job 时拒（`chamber.wafer_owned`）。
- **搬运管理** `Transfer\TransferManager`（sc.xml `Transfer`，自己的扫描线程）：手动、Job、人工恢复共用的唯一执行口，来源 `TransferOrigin` Manual / Auto / Recovery。
  `Submit(TransferRequest)` 当场回 `TransferTicket`（受理带单号和 `Completion` 任务，拒带错误码 + 参数），受理时依次查：没开、没账、站点、槽号、同槽、
  源槽有没有片 / 是不是那一片、目标槽空不空、片归别的 Job（`transfer.wafer_owned`，人工恢复单 Recovery 不查）、槽被别的单锁着、有没有两边都到得了的机械手、手。
  受理就锁源槽、目标槽、片、手；一台机械手一次一单，站点跟在跑的单不重叠的才开始。`TransferRoutine` **先抢目标站点再抢源**，等 `IsSettled`
  （模块收完尾、记完账）才往下走；结果在站点环、晶圆账都收尾之后才出（回执的 `Completion`；结果里 `Picked` 记着取片做完没有，没搬成时分得清错在取片还是放片）。没动手就失败（等不到站点、被撤）：
  把抢到的环撤回锚点（`ITransferStation.CancelTransfer`）、放锁；**动过手才失败：锁留着、站点停在交互中**（`HeldResults`，`NeedsRecovery`），
  人工确认片位、对好账后 `ReleaseHold(单号)`。`Cancel` / `CancelOwner` / `CancelAll`（在动手的发机械手中止）；`Abort()` = 关自动派单 + 全撤。
  **源可以是机械手**（片已经在手上：重启前搬到一半、手动取了没放）：`Source` 写机械手名、`SourceSlot` 写手指号，就用拿着它的那只手，只放片
  （`TransferRoutine` 抢目标 → 准备二 → 放；`TransferOrder.Source` 为 null、`SourceName` 是机械手名）。
  受理时问片归哪个 Job：`JobManager.Current?.OwnerOf(片)`。EC：`StationWaitTimeoutMs`、`ManualWaitTimeoutMs`（手动服务等结果）、`ResultKeepCount`。
  服务 `ITransferService`（`xyz.Service\Transfers`）：`TransferAsync` 下手动单等结果、`ReleaseAsync` 放留着的锁（`transfer.not_held`）。
- **Job** `Job\JobManager`（sc.xml 顶层 `Job`，自己的扫描线程；子节点 `Task` = 机型的任务组件（必须配，继承 `BaseTaskComponent`，35021 是 `TaskComponent`），
  `Scheduler` = `SchedulerComponent`（换 Type 换策略））：SEMI E94 CJ / E40 PJ，决定见 decisions.md「Job」。
  - `JobManager` 里面有 CJ 管理（`ICjManager` / `CjManager`：CJ 队列、历史、E94 状态机、CJ 命令）和 PJ 管理（`IPjManager` / `PjManager`：PJ 队列、片归属、
    E40 状态机、PJ 命令）。两个管理**不拿 JobManager**（只有 CJ 管理拿着 PJ 管理：CJ 的 Stop / Abort 要往下传给 PJ），转换表是各自里的一个 switch
    （带 SEMI 转换号，推动转换的是 `ControlStateAction` / `ProcessStateAction`），转了发 `Transitioned` 事件。牵扯别处的事都在 JobManager：
    建 PJ（查 LoadPort、载具、片、流程配方，任务组件建任务表）、建 CJ 前查名字；PJ 进 ABORTING 撤单 + 腔体中止、PJ 结束任务表收场、CJ 完成告诉 LoadPort；
    CJ 自动转换要的载具好没好、拿没拿走由它查设备给；建好、每条转换、片开始 / 结束加工都由它经 `E94Callback` / `E40Callback` 报
    （走组件层 `Components\Eap\EapNotifier` 单读者派发线程，PJ 结束先于 CJ 完成报）。
  - 任务组件 `Task\BaseTaskComponent`：建 PJ 时照流程配方快照（`ProcessJob.Sequence`，库里 `Find` 给的副本）生成任务表，一片一行（`TaskRow`）：来源 LoadPort 取片 →
    每一站放片、站内任务、取片 → 回片 LoadPort 放片（回片槽建 PJ 时定）。中间的路线整个 PJ 算一次（`BuildRoute`）：这一站的工艺配方取快照、站点组去掉用不了的
    （没装、停用、机械手到不了、跑不了这个配方）、剩下的每个站点都要声明支持用到的任务（`job.station_task_unsupported`）。每一格（`WaferTask`）记状态
    （`WaferTaskState` Waiting / Running / Done / Error / Cancelled）、实际站点和槽、机械手和手、出错原因（改状态给调度用：`Start` / `Done` / `Fail` / `Reset` / `Cancel`）；
    每拍核对片位（不对记 `job.wafer_moved`）。**只管任务表，不碰搬运单、站内操作**。出错停住等人：`Retry`（退回等着做）、`Complete`（人做完了；取放要片在账上正好在这一步做完该在的地方，
    不在回 `job.task_position_mismatch`）。片做没做成看晶圆账（`WaferInfo.ProcessState`），任务表不另记。机型规则不一样就重写 `BuildRoute` / `TasksAt`。
  - 调度引擎 `Scheduling\SchedulerComponent`：照任务表派——站内任务交给站点（`ITransferStation.CheckTask` 只问不动设备、`StartTask` 真起），取片连同后面的放片下一张搬运单；
    先起站内任务、再走机内的片、最后投新片（EC `MaxWafersInMachine`，0 = 不限），站点组按 sc.xml 先后挑第一个能放的；派不出去的下一拍再看。
    执行着的（交给搬运管理的取放、交给站点的站内操作）自己记着，每拍 `Collect` 看做完没有、结果记回任务表：取放没碰到片就失败都退回等着做，
    碰过片才失败按结果的 `Picked` 把出错记在取片或放片上；站内操作被 PJ 中止打断的记未执行。
    只看每一行的许可（`TaskPermission`，PJ 状态定的：暂停、停止就体现在这上面）。
  - 站点任务：`TransferStation\StationTaskAction`（Pick / Place / Process，字符串常量，新站点要新的站内任务自己起名字）、`ITransferStation.SupportedTasks`（默认取放，腔体加工艺）。

  命令（`IJobManager`：建 PJ、建 CJ、CJ / PJ 命令；本地服务和 EAP 调的是同样几个方法、过同一套检查）在调用方线程上当场执行，跟扫描线程用同一把锁
  （等不到回 `job.command_timeout`，EC `CommandTimeoutMs`），做完当场发布。本地建 Job（`JobService.CreateAsync`）也是一个个建 PJ（给了 LoadPort 按它找，没给按载具号找）
  再建 CJ，中途被拒撤掉已建的 PJ。扫描一拍：① 调度引擎收做完的、记回任务表 → ② 核对片位 → ③ PJ、CJ 管理自动转状态（先 PJ 后 CJ，转到不再转）、按 PJ 状态给行定许可 →
  ④ 调度派任务（Manual 不派）→ ⑤ 有变化才发布（`JobListDto` 推送，留存，开机先推一份）。
  不限同时跑几个 CJ、不设 CJ / PJ 个数上限（一个 LoadPort 一个 CJ、一片只归一个 PJ，个数自然有数；S16F21 答 U2 最大值）。
  EAP 按载具号找：`IJobManager.FindControlJobByCarrier` / `FindProcessJobsByCarrier`（从全貌里找，任意线程可调）；PJ 记着建的时候的载具号（`ProcessJob.CarrierId`，E40 报料 PrMtlNameList 也用它）。
  SC：`IsEnable`、`IsPersistent`、`Database`；EC：`CommandTimeoutMs`、`HistoryKeepCount`。服务 `IJobService`（`xyz.Service\Jobs`：建 Job、CJ / PJ 命令、出错任务重做 / 标记完成）。
  **存盘与重启**：每次发布的全貌交给 Job 管理里的一条写库线程（只写最新一份，表 `job_snapshot` 一行 JSON）；`Bind` 时读回上一份，
  CJ 管理 `CloseOutLastRun`：**重启后 Job 不接着跑**——上次没删的 CJ 一律记成中止结束（`CompletedBy` 12、`EndedBy` 13、`Restarted` = true）进历史
  （DTO，排在本次历史后面，一起按 `HistoryKeepCount` 留），上次的历史接着留；机内的片由人确认片位收回后重新建 Job。
  重启收场的 Job 不补报 E40 / E94 事件：开机时 Host 还没连上，报了也发不出去（不算断线，不进缓存）；Host 连上后用 S16F19、S14F1 查得到哪些还在。
  Host 的 S16 / S14 由 EAP 的 E40 / E94 翻成 `IJobManager` 的命令（见 §4「EAP」）。

## 4. 驱动（`xyz.Components\Components\Drivers` + `xyz.Drivers`）

- 品牌驱动壳：抽象基类（`RobotDriverComponent`、`LoadPortDriverComponent`、`RfidDriverComponent`）管 SC 通讯配置、驱动生命周期、`DeviceEvent`
  和触发方法；品牌壳（`RejeRobotComponent`、`FcdLoadPortComponent`、`FcdRfidComponent`）只实现 `CreateDriver()` 和命令工厂。换品牌 = 改 sc.xml 的 Type。
- `xyz.Drivers`：`ICommunication`（串口 / TCP，`CommunicationFactory`）、`IFrameCodec` + `FrameCommunication`（收包泵 + 发送锁）、
  品牌协议放 `Robot\Reje\`、`Loadport\FCD\`、`Rfid\FCD\`（Protocol、FrameCodec、Commands）。驱动回调只改状态，动作由模块扫描线程推进。
- **在途指令要能作废**（2026-10-06）：LoadPort 驱动按指令名占在途位、RFID 驱动只有一个在途位，回复丢了就一直占着，同名指令再也发不出去。
  所以：等回复超时的发起方调 `Abandon`（LoadPort）/ `AbandonInflight`（RFID）让出来；`Close` 作废全部在途；LoadPort 模块动作没做成时 `AbandonAll`。
  作废的指令以失败落终态（Error 写 `Timeout` / `PortClosed` / `Abandoned` 这类英文标记，跟 RFID 一样）；迟到的回复没人认，当无主帧丢掉。
- **断线重连**（2026-10-06）：LoadPort、RFID 驱动组件 Open 过以后，扫描里发现断了就按 EC `ReconnectIntervalMs`（默认 5000）在后台先关后开
  （`Components\Drivers\DriverReconnector`，网口 Connect 会卡几秒，不能在扫描线程上做），断开、恢复各记一次日志；Close 以后不再重连。机械手驱动还没接。
  `FrameCommunication` 一次打开一轮接收泵（`PumpSession`），旧泵出错只停自己那一轮——以前一个全局"在收"标志，旧泵在重连以后才醒会把新泵也停了。
  `LoadPortDriverBase.Open` 先收掉上一轮的收发队列再建新的；发送任务出错只关自己那一轮连接。
- HSMS 协议（`xyz.Secs`）：设备端被动、独占绑定、单会话；S9 只由设备发；`PrimaryReceived` 在收包线程，别在里面同步等 SendAsync
  （`HsmsComponent` 已经把 Host 的报文挪到自己的派发线程上，处理方不用操心这个）。
- 设备侧对象接 EAP 一律开三个口子：命令接口（EAP 和本地服务共用）、上报口（回调属性 + 专用派发线程）、反查口（provider，为 null 走本地规则），
  照 `BaseLoadPortModule` 的 E87 / E84 写；接口放 `xyz.Components\Interfaces`（EAP 组件在组件层，看得到）；细则见 decisions.md「EAP 接入的统一做法」。

### EAP（SECS/GEM，`xyz.Components\Components\Eap`，sc.xml 的 `Eap` 节点）
- **一个 SEMI 标准一个组件**，都是 `EapComponent`（`Eap` 节点）的子节点：`Hsms`（E37 链路）、`E30`（GEM）、`E39`（对象服务 S14）、`E87`（载具）、
  `E90`（片跟踪）、`E40`（PJ）、`E94`（CJ）。每个标准一个子目录（`Eap\E30` …），命名空间照旧 `xyz.Components.Components`；
  几个标准共用的直接放 `Eap` 下：`SecsRead`（读 Host 报文，结构不对抛 SecsException → 链路回 S9F7）、`GemValue`（值 ↔ SECS 格式、时间格式）、
  `E5Error`（ERRCODE + ERRTEXT，只用 E5 的码）、`JobErrors`（Job 的错误码 → E5）、`EapNotifier`。
- **开机**：设备侧都起来以后宿主调 `EapComponent.Bind(LoadPort 们, JobManager)`：链路没启用（`Hsms.IsEnable=False`）什么都不接；
  启用了按 E30 → E39 → E90 → E87 → E40 / E94 接好（各自 `Attach`：登记处理方、挂设备侧上报口），**最后才 `Hsms.Open`**。退出 `EapComponent.Close`（先 Separate，再摘回调）。
- **链路 `HsmsComponent`**：只管连接和分发。`Handle(stream, function, 处理方)` 登记（重复登记开机就抛），Host 的 primary 放进一条派发线程
  按先后处理——处理方可以 await 设备侧的命令；返回 `SecsReply`（`Of(体)` / `Abort`（SxF0）/ `Error(n)`（S9Fn）/ `None`），
  `.Then(动作)` 是回复发出去以后接着做的（先让 Host 看到回复再报事件）。没人登记：整个 Stream 都没人管回 S9F3，否则 S9F5；
  先过 `Gate`（E30 挂的）。连上 / 断开（`LinkSelected` / `LinkClosed`）也排在这条线上通知。设备主动发用 `SendAsync`（没连上抛连接异常）。
- **E30**：通讯状态（链路连上后设备每隔 EC `EstablishCommunicationsTimeout` 发 S1F13，换过一次才算建立，之前 Host 的报文不理）；
  控制状态（EC：`InitialOnline` 默认 False、`OfflineSubState` 默认 HostOffline、`OnlineRemote` 默认 True、`OnlineFailedState`；
  离线时只收 S1F13、S1F17，别的回 SxF0；操作员接口 `RequestOnline` / `RequestOffline` / `RequestRemote`，界面还没做）；
  报告（S2F33 / 35 / 37，S6F15 / 17 / 19 / 21）、报警（S5F1 + 报警事件，S5F3 / 5 / 7）、SV / EC / DV / 事件名单、S2F15 改 EC（全查过再改、不报操作员改常量）、
  时间（EC `TimeFormat`；S2F31 默认只答收下，SC `ApplyHostTime` 为 True 才改本机时钟）、缓存（S2F43 指定缓存哪些——默认什么都不缓存；
  断了通讯开缓存，开着时新报文也进缓存，Host S6F23 要了按先后发或清掉；状态和报文存库，重启接着开着）。S2F41 本机没有远程命令，回 HCACK=1。
  Host 定的报告、开关、缓存范围存 `gem_config`（一行 JSON），缓存报文存 `gem_spool`（SC `Database` 指的库）。
- **E39**：各标准把对象类型登记进来（`IE39ObjectType`：类型名、属性名先后 = 属性号、对象 ID、取属性；建、删、改可选）——Carrier、Port（E87）、
  Substrate、SubstLoc（E90）、ProcessJob（E40）、ControlJob（E94，S14F9 建 CJ 走它）。查要 ON-LINE，改 / 建 / 删要 REMOTE。
- **E87**：挂到每个 LoadPort 上（`IE87Callback` + `IE84Provider`，PortID 按传进来的先后从 1 编）；ID 核对：有 Bind / CarrierNotification 预告且号对上由设备认定，
  没预告的等 Host ProceedWithCarrier，读码失败的等 Host 带端口号给号；认定后 SC `AutoLoad` 自动 Load；槽图跟 Host 给的一样由设备认定，否则等 Host；
  槽图认定 = 料到了：Host 给的片号表写进晶圆账，再通知 E90 建片对象；Host 取消的、核对不过的不要了（卸下来等取），干完 / 中断 SC `AutoUnload` 自动 Unload。
  端口搬运状态按设备的 `LocalTransferState` 加 Host 的停用、不要了的载具算，EC `PortPollMs` 定时重算；预约期间不能改存取方式。
  不支持：CarrierReCreate、CarrierRelease、读写标签（S3F29 / 31）、内部缓冲设备的动作。
- **E90**：挂在晶圆账上（`IE90Callback`），按账报片的位置（在来源 / 机内 / 回到载具）和工艺（要做 / 在做 / 做完 / 中止 / 没做成 / 跳过）、片位有没有片；
  接了 E87 时 LoadPort 上的片等槽图认定才建片对象。片位号：单槽的位置用模块名，多槽的用"模块名.两位槽号"。没有读片号的设备，片号核对不做。
- **E40 / E94**：Host 的 S16F11 / 15 / 5 / 17 / 19 / 21 / 27、S14F9 翻成 `IJobManager` 的命令（来源 Host），被拒的错误码经 `JobErrors` 翻成 E5；
  建 PJ 的料只收一个载具加槽号（槽表空 = 载具上正常的片），载具要已经在端口上；不支持配方参数、暂停事件、改回片地方。
  PJ / CJ 的状态转换（`IE40Callback` / `IE94Callback`）报 PrJobSMTrans01~18 / CtrlJobSMTrans01~13。
- 冒烟：`HsmsSmoke`（链路和分发）、`EapSmoke`（各标准对假 Host、假 LoadPort、真晶圆账、假 Job 管理）。

## 5. 服务（gRPC code-first）

### 加一个服务（清单）
1. 契约 `Shared\xyz.Shared\Services\IXxxService.cs`：
   ```csharp
   [ServiceContract]
   public interface IXxxService
   {
       [OperationContract]
       Task<RpcResponse> DoSomethingAsync(XxxRequest request, CallContext context = default);
   }
   ```
2. 请求 DTO 放 `Shared\xyz.Shared\Dtos`，`[ProtoContract]` + `[ProtoMember(n)]`；返回数据也放 Dtos（普通 POCO，走 JSON）。
3. 实现 `Service\xyz.Service\<领域>\XxxService.cs`：`public class XxxService : BaseService, IXxxService`，构造 `(IReadOnlyList<ComponentBase> roots) : base(roots)`。
4. 注册：`xyz.Service\ServiceExtensions.cs` 的 `AddTransient<IXxxService, XxxService>()`，`xyz.GrpcHost\Program.cs` 的 `MapGrpcService<XxxService>()`。
5. 错误码 + 两个语言包（§6）。
6. 冒烟里直接 new 服务类调方法验证（不起网络），见 `tools\WaferLedgerSmoke` 第 16 节。

### 返回
- 成功：`RpcResponse.Ok()` / `RpcResponse.Ok(JsonHelper.Serialize(数据))`；客户端 `DeserializeData<T>()`。
- 失败：`RpcResponse.Fail(ErrorCodes.Xxx, [参数...])`。**坑**：只传一个字符串 `Fail(ErrorCodes.Xxx)` 会绑到"只有消息"的重载，Code 是空的——
  没参数也要传 `[]`（或像 WaferLedgerService 那样包一个 `Fail(code, params string[] args)`）。
- 服务不往客户端抛异常：设备动作走 `BaseService.RunOperation(...)`（拒绝 → `ActionRejected`、等超时 → `WaitTimeout`、失败 → 操作的 Code/Args）；
  查库、读文件放 `Task.Run` 里 try/catch 转错误码。
- 找模块：`FindModule<T>(name)`，找不到 `ModuleNotFound(name)`。

### 事件推送
- `EventBus.Send(dto, token, retain)`：
  - 全局事件 DTO 带 `public const string EventToken`（"Alarm"、"Ec"、"EquipmentStatus"、"Io"、"Job"、"Log"、"ProcessRecipe"、"RealChart"、"Sequence"、"WaferLedger"）；
  - 模块状态 DTO 用模块名做 token、**留存**（客户端订上立即拿到当前值）；同一模块再推一种 DTO（腔体的部件推送 `ModulePartsDto`）也用模块名，类型不同互不覆盖；
  - "发生了一件事"类用 `retain: false`。
- 组件发 C# 事件（`AlarmChanged`、`ValueChanged`、`WaferManager.Wafer*`），在 `ServiceExtensions` 里桥成 EventBus 消息。
- 变化很密的（整篮 Mapping）只推"哪里变了"的轻通知，让界面自己攒一下再拉（`WaferLedgerChangedDto`）。
- 周期推送放 `xyz.Service\Events\*Publisher`（静态、吞异常、同一个故障只记一次日志）。

### 主界面用到的几处（2026-10-04）
- 系统设置 `SystemSettingsDto` 除了 Modules、Chambers，还带 `LoadPorts`、`Robots`（装配出来、启用的，先后同 sc.xml）：客户端主界面照它生成 LoadPort 页签和默认调度图。
- 机械手站点 `RobotStationDto.Kind`（`StationKind`：LoadPort / Chamber / Other）：`BaseRobotModule` 跟槽数一样从搬运模块表认
  （`TransferManager.TryGetStation` 拿到的是 `BaseLoadPortModule` / `BaseChamberModule` / 别的），表没绑好或不在表里算 Other；变了算状态变化。
- 设备总状态 `EquipmentStatusDto.IsAuto` = 搬运管理的自动派单开着（`TransferManager.IsAutoDispatch`），`EquipmentStatusPublisher.Snapshot(modules)` 算一次。
- 整机操作 `IEquipmentService`（`xyz.Service\Systems\EquipmentService`）：`AutoAsync` 开自动派单（没配搬运管理回 `transfer.not_installed`，
  停用了回 `transfer.disabled`）、`ManualAsync` 关自动派单（Job 不再派新动作，在途的做完）、`StopAsync`：搬运管理 `Abort()`（关自动派单 + 撤单）；正在执行动作的模块直接发 Abort（闲着的不碰，不等中止做完，Data = 直接发了几个），
  在给 Job 做工艺的腔体（`JobManager.IsJobProcess`）、在搬运的机械手除外（由 Job / 搬运管理收场）；最后 Job 全部走中止（`AbortAllAsync`）——
  要先认出哪些腔体在给 Job 做工艺：Job 的中止当场就给它们发中止，之后就认不出来了。

### 启动顺序（`AddXyzServices`）
日志队列 → `SC.Load` → `ComponentLoader.Load` → EC 合并 + 推送桥 → GEM 编号表 → 报警 / 晶圆账推送桥 → PLC Open + Start →
IO 表 Open → Safety Start → 轴 Open → 各模块 Open → **晶圆账开机恢复**（`WaferManager.Restore`：模块登记完槽位之后、开始扫描之前）→ 各模块 Start →
TransferManager Bind + Start → 流程配方库 Bind + 变更推送桥 → 工艺配方库 Bind + 变更推送桥 →
JobManager Bind + Start（模块、搬运管理、配方库都起来之后）→ **EAP Bind**（各标准接到 LoadPort、晶圆账、Job 管理上，最后开 HSMS 链路）→ 设备总状态 / IO 推送 →
数据曲线采样、实时曲线推送 → 注册 gRPC 服务。宿主在这之后才起 Kestrel（HTTP/2，地址取 sc.xml `Rpc` 节点，默认 localhost:5000）。
新组件要 Open/Start 的，按依赖放进这个顺序（读点表的排在 IO 表之后，用 PLC 的排在 PLC 之后）。

## 6. 错误码（`Shared\xyz.Shared\Errors\ErrorCodes.cs`）

```csharp
#region 晶圆账
/// <summary>目标槽上已经有片。Args: [位置, 槽号, 片号]</summary>
public const string WaferSlotOccupied = "wafer.slot_occupied";
#endregion
```
- 值 = `领域.snake_case`，注释写清 Args 顺序；值就是语言包的 key，**加一个码就在 zh-CN、en-US 两个语言包各加一条**（`{0}{1}` 对应 Args）。
- 机型独有的码放机型 `*.Shared\Errors\ErrorCodes.cs`。

## 7. 数据库（`xyz.Database`，SqlSugar）

- `using var db = XyzDb.Create(库名);` 每次用每次建；库名就是 sc.xml `Database` 分组下的节点名（Default = xyz.db、Io = io.db），
  `DataBaseComponent` 装配时注册。没注册的名字回落到默认库。
- 实体：新表 snake_case（`[SugarTable("wafer_adjustment")]`）；流水按天分表：
  `[SplitTable(SplitType.Day)]` + `[SugarTable("xxx_history_{year}{month}{day}")]` + `[SplitField] DateTime OccurredAt`，主键 `long Id = SnowFlakeSingle.Instance.NextId()`；
  写 `Insertable(batch).SplitTable()`，查 `Queryable<T>().SplitTable(起, 止)`，多取一条判断截断。
- 流水组件的套路（报警、晶圆账）：SC `EnableHistory` / `HistoryDatabase` / `HistoryKeepDays`，EC `HistoryBatchSize` / `HistoryBacklogWarning`；
  `OnSettingLoaded` 建 Channel 和写库任务（这时不碰库）；每天第一次写之前整张删过期日表。写库失败只记日志，不影响设备。
- 量小、要马上查到的（人工调整记录）直接同步写一张不分表的表，用到时 `CodeFirst.InitTables` 建表。
- **晶圆账存盘**（SC `IsPersistent`，EC `SnapshotIntervalMs` 默认 500）：表 `wafer_current` 存"现在"每片一行（内部标识、位置、来处、载具、状态），
  账一变（都走 `RecordHistory`）标脏，存盘线程隔一会儿整张重写（事务里删了再插）；**开机恢复之前不写**（开机那一刻的空账不能冲掉上次的）。
  `Restore()`（启动顺序里模块 Open 之后、Start 之前）：腔体、机械手上的放回去（同一个内部标识，流水记 `Restored`），**LoadPort 上的不恢复，以开机 Mapping 为准**，
  重启前在加工的记成中止，放不回去的（位置没装、越界、槽上有片）丢掉记告警。宿主退出（`ApplicationStopping`）调 `StopSnapshot()` 最后存一次。
- Job 存盘同理：`job_snapshot` 一行最新全貌（见 §3 Job）。
- GEM（E30）掉电保持：`gem_config` 一行 JSON（Host 定的报告、链接、关掉的事件和报警、缓存范围、缓存状态），Host 改了就同步写；
  `gem_spool` 缓存的报文一条一行（雪花号主键 = 先后，体是 SECS-II 编码的字节），断了通讯才写。见 §4「EAP」。

## 8. 配置文件

- 源：`Service\xyz.Configs\Config\sc.xml`（只有这一个；UTF-8 BOM、Tab 缩进、节点带中文注释）。编译时拷到宿主 `bin\...\net10.0\Config\sc.xml`（**运行时读的是这份**）。
- 启动时在同目录生成：`ec.xml`、`EcDefinitions.xml`、`SvDefinitions.xml`、`AlarmDefinitions.xml`、`EventDefinitions.xml`、`DvDefinitions.xml`；
  IO 点表 `Config\IO\*.csv` 来自机型工程。
- 节点顺序：System → Rpc → EC → Database → Alarm → Log → WaferManager → Plc → Io → Safety → DataChart → RealChart →
  LoadPort → Robot → Transfer → Sequence → ProcessRecipe → Job → Eap（Hsms、E30、E39、E87、E90、E40、E94）→ Chamber → …（平台组件在前，模块在后）。加组件时把全部 Value 和注释写进 sc.xml。
- 外部工具 ScEdit（`D:\tools\ScEdit`，源码不在本仓库）按 `[Component]` / `[SCEditor]` 编辑 sc.xml，备份到 `Config\backup\`（已 gitignore）；
  机械手站点表、工艺配方字段表（`ProcessRecipe.Fields`，「配方字段」页，规则跟后端一样）有专门的表格页。改了 ScEdit 要跑它的 build.ps1 发布到 app。

## 9. 日志

- `LogHelper.Debug/Info/Warn/Error(module, message)`（module 一般用组件 `Name` / `FullPath`），写 `Log\xyz-日期.log` 和控制台；
  Warn 以上进 `LogQueue` 推给客户端顶栏。中文、说清楚是什么、为什么、接下来会怎样（如"人工调整记录写库失败（账已经改了）"）。

## 10. 线程和错误处理

- 共享状态：`private readonly object _gate = new(); lock (_gate)`；**事件在锁外发**；给调用方的是副本/快照。
- 单读者 `Channel<T>` 做后台写库、EAP 回调；`Interlocked` 计数；`volatile` 快照替换。
- 配置错、装配错：抛 `InvalidOperationException`（带节点名和值），开机就暴露。运行期：返回 bool / null / 结果枚举 / 操作的 Code+Args，
  服务层翻成错误码；循环和计时器里 catch 住记日志，不让设备线程崩。
- 解析用 `CultureInfo.InvariantCulture`；计时用 `Stopwatch` / `Environment.TickCount64`。
