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
- 生命周期：`InitComponent()`（**不动硬件**的开机初始化：连接、登记晶圆账、挂事件、输出回安全态；先子后己、按 InitOrder 递归到每一层子组件，
  一个子组件没做成不耽误别的、汇总返回 `bool`；开机由宿主对每个模块调一次，子组件跟着基类走，**父组件不点名**，组件有事就重写、记得调 base）、
  `Abort()`（只停，不清报警）、`Reset()`（先子，再清本组件报警）；Abort / Reset 模块把返回类型收窄成 `ModuleOperation?`。
  **要动硬件的初始化（回原点）是模块的 `InitModule()`**（`BaseModule`，返回 `ModuleOperation?`，人或调度才调，开机不调，不递归子组件；
  LoadPort / Robot / Chamber 基类里 = `Home()`，机型要多做别的步骤就重写）。组件初始化里不放会动轴、动气缸的事——部件怎么回零、什么先后，
  写在所在模块的 Home 操作里去驱动。`BaseModule.Open()` 已经没有了；PLC、IO、HSMS、轴（`Open(IPlc)`）各自的 `Open` 还是装配层按类型调；
  气缸 / 阀的 `Open()` / `Close()` 是开合动作，不是生命周期。
- 配置钩子：`OnSettingLoaded(ModuleConfig)`——[SCEditor] 灌完值后调，配置不对就抛异常（开机直接报出来）。
- 单例：`public static X? Current { get; set; }` + 构造里 `Current = this;`（报警、EC、System、Log、Rpc、WaferManagerComponent、Io、Safety、
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
- `Interfaces` 下除了组件自己的（IPlc……），还有设备侧给 EAP 的命令接口和上报口：`ILoadPort`、`ICarrier`（经 `ILoadPort.Carrier` 拿到，一个端口一个载具组件）、`ICarrierIdReader`（载具读码器，平台默认 `RfidDriverComponent` 实现）、`IE87Callback`、`IE84Callback`、
  `IE84Provider`、`IJobManager`、`IE40Callback`、`IE94Callback`、`IE90Callback`（挂在晶圆账 `WaferManagerComponent.E90Callback` 上）；它们用到的 `E84Timer`、`LoadPortTransferState`、CJ / PJ 的状态和命令在 `Enums`，
  Job 的请求（`ProcessJobSpec`、`ControlJobSpec`，本地、Host 共用）和载具快照 `CarrierInfo` 在 `Models`；命令结果用 xyz.Shared 的 `HandleResult`
  （失败时 `ErrorMessage` 放错误码、`Args` 放参数）。
  实现还在模块层（`BaseLoadPortModule`、`CarrierComponent`、`JobManager`）；EAP 组件写在组件层，直接用这些接口。

## 3. 模块（`xyz.Modules`）

- **目录**（2026-10-09，用户："该归类就是归类用文件夹"）：模块的域目录（`Loadport`、`Clean`、`Robot`……）里，本体（`Base*Module`、`*Component`）留在根，其余按类别放子文件夹
  `Enums\`、`Models\`、`Operations\`、`StateMachines\`（`Job` 下的 `Model\`、`Task\` 是同一个做法）；**只归类，命名空间不改**（一般 `xyz.Modules`，状态码和动作枚举 `xyz.Modules.Enums`，状态表 `xyz.Modules.StateMachines`）。
  `Loadport` 已分好：`Enums\`（SlotPickOrder、PodPresenceSource、LoadPortCommandStep、LoadPortState 加 LoadPortAction）、`Operations\`（LoadPortCommandOperation）、`StateMachines\`（LoadPortStateTable）；
  `Clean`、`Robot`、`E84` 还平铺，改到那一块时照这个分。
- `BaseModule`：`abstract int State`（子类加 `[VariableMark(SV, Int, ...)]`，初值 `ModuleState.NotInit`）、`InitModule()`（动硬件的模块初始化，默认返回 null）、
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
- 动作失败（被人中止顶掉的不算）在操作终结时由 `RaiseActionFailedAlarm`（`protected virtual`，机型可重写：自己要管的情况先判、报了就 return，其余交给 base，整套换掉不调 base；在模块锁里，只报警不等待）报警，一次失败只报一条：机械手、腔体报 `ControlledStopAlarm`；LoadPort 超时按动作报各自的超时报警（Load / Unload / Home=初始化 / 夹紧 / 松开 / 复位 / 中止，跟 EC 各动作超时一一对应），不是超时的报 `ControlledStopAlarm`（显示为"LoadPort 动作失败"）。设备报错每拍 `RaiseAlarm(XxxDeviceAlarm)`。
- 站点类：`BaseTransferStationModule`（SlotCount、传片环 PrepareTransfer → Transferring → TransferComplete）、
  `BaseLoadPortModule`（子组件按类型找 Driver / RFID / E84；`InitComponent` 里先登记晶圆账槽位、挂驱动的主动事件，再由基类把子组件（驱动、读头、E84）各自初始化——
  连接、E84 输出回初始写在它们自己的 `InitComponent` 里，端口不点名；RFID、驱动这一次没连上也照样往下走，
  返回 false 只为开机日志看得到，之后由驱动组件按间隔重连；`InitModule()` = `Home()`；**设备状态查询在平台**：每拍一条 GET:STATE，超过 EC `QueryDataTimeOut` 没回就作废这一条、
  Status 清空、下一拍重发，超时 / 恢复各记一次日志，机型不用写；**载具交给子组件 `CarrierComponent`**（sc.xml 每个 LoadPort 下必配一个 `Carrier` 节点，缺了开机抛 `InvalidOperationException`；`ILoadPort.Carrier` 是它的 `ICarrier` 口，读码、槽图认定状态、取放状态、E87 载具上报都在它里面，见 decisions.md「LoadPort 的载具收进 CarrierComponent」）：**在位二选一**（Carrier 的 SC `PresenceSource`，默认 Query）：Query 看状态查询的在位、到位两位（端口每拍用 `Carrier.Sense` 喂进去），
  都亮放好、都灭拿走、一亮一灭或查不到不算变化，Event 看 PODON / PODOF（端口的 `SetDeviceReportedPlaced` 转给 `Carrier.SetDeviceReportedPlaced`，机型有别的上报路子也调它），只在扫描线程判边沿，
  判出来的叫 `Carrier.IsArrived`（载具到了，推给界面的"在位"也是它；状态查询的原始位叫 `IsPresent` / `IsPlaced`，`LoadPortStatus` 的开关量一律 `Is` 开头）；动作没做成（失败、超时、被顶替）在 `OnOperationCompleted` 里把驱动的在途指令全部作废；Idle 一律当"门关好、没 Load"，门不确定落 NotInit：
状态表 Reset / Abort 写最保守的（出错 / 没初始化复位、Loaded 复位、中止除 Idle / Error 外一律 NotInit），`Begin` 记下动作前的状态，做成后
`SetStateByDoor` 对动作前门没在动的（Loaded、交互环）按状态查询改：门开且载具在 → Loaded，门关 → Idle，查不到 → 保持 NotInit；
Load 先过联锁虚方法 `LoadInterlock()`（在 `Begin` 里查，默认要 `Carrier.IsArrived`，机型有别的条件重写、先调 base）；**7 个动作平台给默认实现**（`LoadPortCommandOperation`：发驱动指令 → 等完结 → 超时判失败，Load 成功调 `UpdateSlotMap`，它转给 `Carrier.UpdateSlotMap`；动作做成 / 失败时端口再告诉 Carrier：`StartAccess` / `EndAccess` / `MarkAccessStopped`；Unload 分两个口径：自动跑货（Job 干完自动卸、E87 放行）走 `Unload()`，SC `AutoRunMapOnUnload` 决定带不带图对账；手动页面走 `UnloadManually()`，一律不扫图、不对账——Mapping 是 LoadPort 硬件自带的，SC 只管自动跑货用不用它），每拍最后 `PublishState`，机型类只在动作不一样时重写；
  E84 子组件 SC `IsEnable`=False（本机没接搬运车）时 `E84` 属性为 null，端口当没有 E84：不初始化、不每拍推、不读写 IO
  （基类递归会带着 E84 一起初始化，所以 E84 自己的 `InitComponent` 也拦 `IsEnable`）——
  跟 EC `E84Enabled`（装了以后现场在线开关交接）分开）、
  `BaseRobotModule`（sc.xml 子节点 `Stations` 读站点表：Number、Y、Direction、Arms；推送的站点表还带槽数、站点类型 Kind；`Pick/Place(arm, 站点名, slot)` 成功后改晶圆账）、
  `BaseChamberModule`（`InitComponent` 只登记晶圆账，子组件照基类递归；`InitModule()` = `Home()`，部件回零的先后写在机型的 Home 操作里）。
- **手动部件是通用的**（组件自己声明，模块不认具体硬件）：组件类标 `[PartKind("Axis")]`（派生类继承；现有 `Axis` 轴、`TwoState`
  双作用气缸、`OneState` 阀 / 喷嘴），属性标 `[LiveValue]`（推给界面的实时数据，浮点按 `Decimals` 位取整，默认 3），
  方法标 `[ManualAction]`（返回 bool = 指令发没发出去；`Priority = true` 停止类，`Release = "Stop"` 按住类）。
  做没做完由组件自己说：`ComponentBase.ActionState`（基类默认已做完，自己管动作到完成的轴、气缸、阀重写；2026-10-07 用户："为什么是腔体判断，不应该"，去掉了 `IActionComponent`）。`xyz.Modules\Parts\PartCatalog` 照模块的组件树（先父后子）
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
  **Host 远程管配方**：流程配方库实现 `Interfaces\ISequenceComponent`：`SequenceNames`、`ExportSequence`、`AcceptsSequence`、`ImportSequence`、`DeleteSequence`；
  工艺配方库实现 `Interfaces\IProcessRecipeComponent`：`ProcessRecipeNames`、`ExportProcessRecipe`、`AcceptsProcessRecipe`、`ImportProcessRecipe`、`DeleteProcessRecipe`。
  导出按名字取库里的副本转 JSON（工艺配方步骤是字段名 → 值，`ProcessRecipeStep.Attributes` 是存 XML 用的、JSON 里不带）；导入按名字新建或覆盖说明和步骤，
  检查、规整跟本地一样，JSON 里的编号 / 版本 / 人和时间不管；满了 `*.full`、读不出来 `*.body_invalid`，删除时没有名字回 `*.name_not_found`。
  两个 `Accepts` 方法只看 JSON 的样子，Host 下新名字时分库用。
  上报口 `E30Callback`（`IE30Callback`）：分别调用 `SequenceChanged` / `ProcessRecipeChanged`；建、改、删、改名（旧名删 + 新名建）都在 `Report` 里经 `EapNotifierComponent` 报。
  配方服务改配方（建、改名、存、删）之前先问 `E30RecipeComponent.Current?.IsLocalEditLocked`，锁着回 `recipe.locked_by_host`。
  还没做：腔体按工艺配方的步骤真的去转、去喷（35021 的 Process 还是定时模拟，只认名字）；字段作用到哪个设备（AO 等，本来就配在 sc 腔体下面）到时再定。
- **加工口** `Process\IProcessStation`（`BaseChamberModule` 实现）：手动起工艺和 Job 走同一个口子。`CheckProcess(ProcessRequest)` 只问不动设备
  （没配方 → 配方对不上这个腔体 → 槽号 / 片号对不上 `chamber.wafer_mismatch` → 状态不允许 / 在忙 `module.action_rejected`），
  `StartProcess` 在锁里 Begin，起了把账上的片标成 InProcess，做完（`OnOperationCompleted`）标 Completed / Failed / Aborted。
  机型只写 `CreateProcessOperation(ProcessRequest)`（请求里带配方快照）。
  `ChamberService.ProcessAsync` 先取库里的配方快照，腔里的片归某个 Job 时拒（`chamber.wafer_owned`）。
- **搬运管理** `Transfer\TransferManager`（sc.xml `Transfer`，自己的扫描线程）：手动、Job、人工恢复共用的唯一执行口，来源 `TransferOrigin` Manual / Auto / Recovery。
  `Start(TransferRequest)` 当场回 `HandleResult<TransferRoutine>`（成功给实际设备操作，拒绝给错误码 + 参数）；请求只是参数，不建立搬运单、单号、回执或结果历史。
  启动时查：启用、晶圆账、站点、槽号、同槽、源片标识、目标槽空闲、Job 归属（人工恢复 Recovery 可搬 Job 的片）、资源占用、机械手可达与手臂可用。
  调度直接查搬运管理的 `IsSlotLocked` / `IsRobotInTransfer`，手臂占用在搬运启动时内部校验，不另建占用快照类。
  源槽、目标槽、片、手一起占用；机械手忙或站点与正在执行的操作重叠就拒绝，不另排搬运队列。自动任务仍在原任务表等待下一拍。
  `TransferRoutine` **先抢目标站点再抢源**，子操作 `IsSettled`（模块收完尾、记完账）才继续；取片确认记 `HasPicked`，整趟资源收尾及设备中止确认后才置自身 `IsSettled`。
  没动手就失败：把抢到的环撤回待命态（`ITransferStation.CancelTransfer`）、释放资源；**动过手才失败：保留资源、站点停在交互中**（`HeldOperations`、`NeedsRecovery`）。
  人工确认片位、对好账后 `ReleaseHold(晶圆内部标识)`。`Cancel(操作)` / `CancelOwner` / `CancelAll` 请求由搬运扫描线程执行中止；`Abort()` = 关自动调度 + 全部中止。
  **源可以是机械手**（片已经在手上：重启前搬到一半、手动取了没放）：`Source` 写机械手名、`SourceSlot` 写手指号，就用拿着它的那只手，只放片
  （`TransferRoutine` 抢目标 → 准备二 → 放；`Source` 为 null、`SourceName` 是机械手名）。
  启动时问片归哪个 Job：`JobManager.Current?.OwnerOf(片)`。EC：`StationWaitTimeoutMs`、`ManualWaitTimeoutMs`（手动服务等操作收尾）。
  服务 `ITransferService`（`xyz.Service\Transfers`）：`TransferAsync` 启动手动传片并等待操作收尾、`ReleaseAsync` 按晶圆标识释放保留资源（`transfer.not_held`）。
- **Job** `Job\JobManager`（sc.xml 顶层 `Job`，自己的扫描线程；子节点 `Task` = 机型的任务组件（必须配，继承 `BaseTaskComponent`，35021 是 `TaskComponent`），
  `Scheduler` = `SchedulerComponent`（换 Type 换策略））：SEMI E94 CJ / E40 PJ，决定见 decisions.md「Job」。
  - `JobManager` 里面有 CJ 管理（`ICjManager` / `CjManager`：`Dictionary<string, CjEntity>`，key 为 CJ ID，每个实体包含 `ControlJob` 和自己的 `CjStateMachine`）
    和 PJ 管理（`IPjManager` / `PjManager`：`Dictionary<string, PjEntity>`，key 为 PJ ID，实体包含 PJ 对象及独立状态机，并管理晶圆归属）。CJ 管理不依赖 PJ 管理，不处理 PJ。
    结构按用户提供的参考工程统一：`CjEntity` 是私有实体，`ControlJobs` 只返回 CJ 对象；`Add(ControlJob)` 只注册，`Queue(ControlJob)` 才入队，
    `Remove(ControlJob)` 检查 ID 和对象引用，旧对象不能移除同 ID 的新对象；`Get(id)` 从字典查找。
    `CjStateMachine` 继承 `BaseStateMachine<ControlJobState, ControlStateAction>`，不持有 Job；类内 BuildTransitions 建字典，
    key 为 `(state, action)`，value 为 `StateTransition<ControlJobState>`（TargetState、ProcessState、OnEntry、PreCheck、Execute、OnExit、ErrorHandler）。
    基类提供 CurrentState / OnStateChanged / StateChange，执行检查与动作；管理器订阅状态变化，同步 CJ 状态和时间并发送 StateChanged。
    StateChanged 传 (ControlJob, ControlJobState, int e94TransitionNumber)，转换号在转换发生时作为局部变量计算并传给上报；ControlJob 不保存 TransitionNumber。
    管理器提供以 ControlJob 对象为参数的 Queue / Select / Activate / Pause / Resume / Complete / Abort / FinishAbort / Rollback 等动作，
    统一查注册实体再调用 StateChange；不提供 Advance / NextTrigger，不向外暴露实体状态机。
    PJ 同样用 Add / Remove / Get / ProcessJobs 管理字典；Add 只登记，Queue 才入队并登记晶圆归属，Remove 检查对象引用并释放归属。
    PjStateMachine 同样继承 BaseStateMachine，类内 BuildTransitions 定义 E40 转换表；保留原 E40 数值，新增内部 Created=-1（不向 Host 上报）。
    PJ 的准备、等待 Start、处理完成后回片、暂停 / Stop / Abort 收尾路径保留；暂停前的恢复目标保存在各自状态机的转换表，不存进 ProcessJob。
    PjManager 提供以 ProcessJob 为参数的 Queue / Setup / WaitForStart / Activate / Start / Complete / Finish / Pause / FinishPause / Resume / Stop / FinishStop / Abort / FinishAbort / Dequeue，
    不再提供字符串 Execute、公开 Fire 或自动推进。StateChanged 传 (ProcessJob, ProcessJobState, int e40TransitionNumber)，编号只作为本次通知参数。
    JobManager 根据任务与设备收尾进度提交 PJ 动作，按 PJ 状态设置行的许可；PJ 结束时在 JobManager 中移除登记、关闭任务表并存盘。
    内部状态与参考一致：Created=0、Queued=1、Selected=2、Executing=3、Paused=4、Aborting=5、Aborted=6、Completed=7、WaitingForStart=8。
    本设备补全 WaitingForStart 手动启动、Stop 收尾和 E94 删除路径；JobManager 负责外部命令及 PJ 协调。
    CJ 字典保持添加顺序，不提供 HeadOfQueue 或主动重排；外部 HOQ 命令明确拒绝。
    DTO.State 使用内部状态，DTO.E94State 用于 Host 上报和历史库（原 E94 0~5）；Aborting 上报中止前的状态，Aborted 上报 COMPLETED=5。
    JobManager 负责建 PJ、解析和关联 CJ 的 PJ 集合，以及 CJ 取消 / Stop / Abort 时协调 PJ；PJ 进 ABORTING 中止设备、PJ 结束任务表收场、CJ 完成通知 LoadPort；
    载具就绪、PJ 执行结束、载具离位时由 JobManager 提交相应 action，当前状态是否允许转换由状态表决定；建好、每条转换都由它经 `E94Callback` / `E40Callback`（片开始 / 结束加工 E40 没有事件，由 E90 照晶圆账报） 报
    （放进 EAP 的上报派发组件 `EapNotifierComponent.Current`，跟所有上报一条线程按先后发，PJ 结束先于 CJ 完成报）。
  - 任务组件 `Task\BaseTaskComponent`：建 PJ 时照流程配方快照（`ProcessJob.Sequence`，库里 `Find` 给的副本）生成任务表，一片一行（`TaskRow`）：来源 LoadPort 取片 →
    每一站放片、站内任务、取片 → 回片 LoadPort 放片（回片槽建 PJ 时定）。中间的路线整个 PJ 算一次（`BuildRoute`）：这一站的工艺配方取快照、站点组去掉用不了的
    （没装、停用、机械手到不了、跑不了这个配方）、剩下的每个站点都要声明支持用到的任务（`job.station_task_unsupported`）。每一格（`WaferTask`）记状态
    （`WaferTaskState` Waiting / Running / Done / Error / Cancelled）、实际站点和槽、机械手和手、出错原因（改状态给调度用：`Start` / `Done` / `Fail` / `Reset` / `Cancel`）；
    当前设备操作直接挂在 `WaferTask.Operation`（不序列化，结束时清空），不另建执行任务表；任务组件管状态，调度负责启动和收进度。
    每拍核对片位（不对记 `job.wafer_moved`）。出错停住等人：`Retry`（退回等着做）、`Complete`（人做完了；取放要片在账上正好在这一步做完该在的地方，
    不在回 `job.task_position_mismatch`）。片做没做成看晶圆账（`WaferInfo.ProcessState`），任务表不另记。机型规则不一样就重写 `BuildRoute` / `TasksAt`。
  - 调度引擎 `Scheduling\SchedulerComponent`：从每行当前 `WaferTask` 执行；站内任务直接交给站点 `StartTask`（内部校验，拒绝就等待；`CheckTask` 只供单独询问），取片先选定并占住后续放片目标，再启动实际搬运操作。
    先起站内任务、再走机内的片、最后投新片；并发由目标槽、机械手和站点占用自然限制，不设机内片数上限。站点组按 sc.xml 先后挑第一个能放的，派不出去下一拍再看。
    每拍 `Collect` 直接读当前任务的 `Operation`，没有 `_moves` / `_works` 或 `Move` / `Work` 执行记录。取片确认后完成取片格，操作引用转到放片格，整行同时只有一格 Running。
    没碰到片就失败退回等待；动过片才失败将当前取片或放片格记 Error；站内操作被 PJ 中止打断的记未执行。
    不维护每拍槽位、机械手的重复占用集合，直接查询搬运管理；调度按先后尝试空闲且可达的机械手，手臂选择和资源校验只在搬运执行口做一次。
    工艺日志在任务状态方法里直接写，不再经任务开始 / 结束事件转发到 JobManager；EAP 片加工上报仍由 E90 照晶圆账报。
    只看每一行的许可（`TaskPermission`，PJ 状态定的：暂停、停止就体现在这上面）。
  - 站点任务：`TransferStation\StationTaskAction`（Pick / Place / Process，字符串常量，新站点要新的站内任务自己起名字）、`ITransferStation.SupportedTasks`（默认取放，腔体加工艺）。

  创建入口：`CreateProcessJobAsync(loadPort, pjName, slots, sequence, lotId)` 独立创建 PJ，LotId 跟着 PJ 进入快照和 `process_job` 记录；
  创建方法直接接收参数，不再使用 `ProcessJobSpec` / `ControlJobSpec`；服务通信仍用请求 DTO。CJ / PJ 创建参数、请求 DTO 和运行对象不存 AutoStart，
  启动方式由 Job 节点的 SC `ProcessJobAutoStart`（默认 True）和 `ControlJobAutoStart`（默认 False）决定：True 准备好直接开始，False 等待对应的 Start 命令。
  E40 建 PJ 报文的 PRPROCESSSTART、E94 建 CJ 的 StartMethod 只校验格式、不覆盖 SC；查询、上报和历史记录中的 AutoStart 反映设备配置。
  CJ / PJ 不保存 CarrierInstance，CJ 完成后检测到来源 LoadPort 的 Carrier.IsArrived=False 才删除；载具仍在位时保留结果。
  载具能不能分给 Job 由 `BaseLoadPortModule.CanAssignCarrierToJob` 判断（模块启用、载具到位、IsLoaded（正被机械手取放也算），接了 EAP 时槽图被 Host 认定；只管排活，机械手能不能进站看 `CanPrepare`）；Job 创建、定片、回片目标选择、CJ 启动直接读取该属性，不在 Job 内解释 LoadPort 状态码。
  **料没到先建 PJ**：Host 按载具号建（不给 loadPort）时载具不在口上或还不能取片，PJ 照样建、排队，只记载具号和要的槽号（`ProcessJob.Slots`，空 = 料到了取全部有片的槽），
  任务行空着（`IsWaitingForMaterial`）；扫描每拍先定片（`AssignWaitingProcessJobs` → `AssignWafers`，跟当场建同一段检查），定好挂任务表、`IPjManager.RegisterWafers` 登记片归属、
  给 CJ 填口；定不了报 `MaterialUnusableAlarm`、日志写原因、PJ 留在排队。CJ 收没定片的 PJ 时口空着、按载具号查重（`job.carrier_busy`），要下面的 PJ 都定了片才转执行。
  `CreateControlJobAsync(loadPort, processJobs, ...)` 用 PJ 名称集合关联 CJ，指定口与任何 PJ 不匹配（或 PJ 还没定片）时整个拒绝、PJ 不受影响；
  `CreateJobAsync(loadPort, processJobs, ...)` 使用已有 PJ，内部调用创建 CJ，回 `JobCreatedDto`，不再新建 PJ 或任务行。
  `IPjManager` / `ICjManager` 提供对象状态动作；对外 `IJobManager` 提供 CJ/PJ 明确命令方法。CJ 的 Stop / Abort / Cancel 可选择 SaveJobs / RemoveJobs，
  PJ 的 Cancel 只取消未开始的 PJ、释放晶圆归属。Start / Pause / Resume / Stop / Abort / Cancel 异步方法各自直接处理对应动作；枚举命令入口只负责选择这些方法，不作为内部公共执行入口。
  命令（本地服务和 EAP 过同一套检查）在调用方线程上当场执行，跟扫描线程用同一把锁
  （等不到回 `job.command_timeout`，EC `CommandTimeoutMs`），成功执行后当场发布；创建校验或状态命令被拒不发布。检查、加锁、执行、发布、异常处理直接写在各入口里，
  不再使用 Execute / WithProcessJob 委托包装；PJ 创建逻辑直接在 CreateProcessJobAsync 内。本地建 Job（`JobService.CreateAsync`）也是一个个建 PJ（给了 LoadPort 按它找，没给按载具号找）
  再建 CJ，中途被拒撤掉已建的 PJ。PJ / CJ 创建检查 Queue 结果，失败撤销本次登记及任务或关联；CJ 失败保留先前独立创建的 PJ。扫描一拍：① 调度引擎收做完的、记回任务表 → ② 核对片位 → ③ JobManager 分别检查 PJ、CJ 推进条件，提交状态动作（先 PJ 后 CJ，转到不再转），再按 PJ 状态给行定许可 →
  ④ 调度派任务（Manual 不派）→ ⑤ 直接发布当前快照（`JobListDto` 推送，留存，开机先推一份）。不使用 dirty 标记、快照版本号或任务表版本比较，每拍发布并交给存库线程合并写入。
  不限同时跑几个 CJ、不设 CJ / PJ 个数上限（一个 LoadPort 一个 CJ、一片只归一个 PJ，个数自然有数；S16F21 答 U2 最大值）。
  EAP 按载具号找：`IJobManager.FindControlJobByCarrier` 在 Job 锁内调用 `CjManager.FindByCarrier`，从 CJ 字典读取并返回 DTO 副本；`FindProcessJobsByCarrier` 在锁内读取当前 PJ，包括保留在 CJ 下的已结束 PJ。Snapshot 查询在锁内生成 DTO，不缓存 `_snapshot`。PJ 记着建的时候的载具号（`ProcessJob.CarrierId`，E40 报料 PrMtlNameList 也用它）。
  SC：`IsEnable`、`ProcessJobAutoStart`、`ControlJobAutoStart`、`IsPersistent`、`Database`；EC：`CommandTimeoutMs`。服务 `IJobService`（`xyz.Service\Jobs`：建 Job、CJ / PJ 命令、出错任务重做 / 标记完成）。
  **存库与重启**（2026-10-07 用户："界面上显示的就是数据库的数据，该存库就存库"）：CJ、PJ 各一张表，一个 Job 一行（`control_job` / `process_job`，
  实体 `ControlJobEntity` / `ProcessJobEntity` 继承 `BaseEntity`：自增行号、CreatedTime = 建的时刻、UpdatedTime = 最后写的时刻；PJ 行记着 CJ 的行号 `ControlJobRowId`，
  每片的任务明细是 `JobWaferDto` 列表的 JSON 放在 `Wafers` 列）。每次发布把全貌里的 CJ、PJ 交给 Job 管理里的一条写库线程（同一个 Job 只写最新一份；
  第一次插一行、行号记在 `ControlJob.RowId` / `ProcessJob.RowId` 上，之后按行号更新），删掉的 CJ、结束的 PJ 在转换那里单独交最后一次。
  内存里不留历史、全貌里没有历史，界面看历史查库（查询服务等 Job 页定了再加）。`Bind` 时 `CloseOutLastRun`：**重启后 Job 不接着跑**——
  库里还没删的 CJ 记成中止结束（`CompletedBy` 12、`EndedBy` 13、`Restarted` = true；完成了还没删的只补 #13），没结束的 PJ 记成中止（#16），
  任务明细留着重启前做到哪；机内的片由人确认片位收回后重新建 Job。两张表不清理（量小）。
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
- **断线重连**（2026-10-06）：LoadPort、RFID 驱动组件 `InitComponent` 过以后，扫描里发现断了就按 EC `ReconnectIntervalMs`（默认 5000）在后台先关后开
  （`Components\Drivers\DriverReconnector`，网口 Connect 会卡几秒，不能在扫描线程上做），断开、恢复各记一次日志；Close 以后不再重连。机械手驱动还没接。
  `FrameCommunication` 一次打开一轮接收泵（`PumpSession`），旧泵出错只停自己那一轮——以前一个全局"在收"标志，旧泵在重连以后才醒会把新泵也停了。
  `LoadPortDriverBase.Open` 先收掉上一轮的收发队列再建新的；发送任务出错只关自己那一轮连接。
- HSMS 协议（`xyz.Secs`）：设备端被动、独占绑定、单会话；S9 只由设备发；`PrimaryReceived` 在收包线程，别在里面同步等 SendAsync
  （`HsmsComponent` 已经把 Host 的报文挪到自己的派发线程上，处理方不用操心这个）。
- 设备侧对象接 EAP 一律开三个口子：命令接口（EAP 和本地服务共用）、上报口（回调属性；报的时候放进 `EapNotifierComponent.Current`，不自己起线程）、反查口（provider，为 null 走本地规则），
  照 `BaseLoadPortModule` 的 E87 / E84 写；接口放 `xyz.Components\Interfaces`（EAP 组件在组件层，看得到）；细则见 decisions.md「EAP 接入的统一做法」。

### EAP（SECS/GEM，`xyz.Components\Components\Eap`，sc.xml 的 `Eap` 节点）
- **一个 SEMI 标准一个组件**，都是 `EapComponent`（`Eap` 节点）的子节点：`Hsms`（E37 链路）、`Notifier`（`EapNotifierComponent`，上报派发，必须配）、`E30`（GEM）、`E39`（对象服务 S14）、`E87`（载具）、
  `E90`（片跟踪）、`E40`（PJ）、`E94`（CJ）。每个标准一个子目录（`Eap\E30` …），命名空间照旧 `xyz.Components.Components`；
  几个标准共用的直接放 `Eap` 下：`SecsRead`（读 Host 报文，结构不对抛 SecsException → 链路回 S9F7）、`GemValue`（值 ↔ SECS 格式、时间格式）、
  `E5Error`（ERRCODE + ERRTEXT，只用 E5 的码）、`JobErrors`（Job 的错误码 → E5）。
- **开机**：设备侧都起来以后宿主调 `EapComponent.Bind(LoadPort 们, JobManager, 流程配方库, 工艺配方库)`：链路没启用（`Hsms.IsEnable=False`）什么都不接；
  启用了按 E30 → E39 → Recipe → E90 → E87 → E40 / E94 接好（各自 `Attach`：登记处理方、挂设备侧上报口），**最后才 `Hsms.Open`**。退出 `EapComponent.Close`（先 Separate，再摘回调）。
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
- **E39**：各标准把对象类型登记进来（`IE39ObjectType`：类型名、属性名先后 = 属性号、对象 ID、取属性；建、删、改可选）——
  Substrate、SubstLoc（E90）、ProcessJob（E40）、ControlJob（E94，S14F9 建 CJ 走它）。查要 ON-LINE，改 / 建 / 删要 REMOTE。
- **Recipe**（`Eap\Recipe\E30RecipeComponent`，E30 的工艺程序管理）：分别挂到 `ISequenceComponent`、`IProcessRecipeComponent`，上报口是 `IE30Callback`。
  **配方号就是配方名**（不加前缀，用户定的；两个库的名字不会重），按名字到两个库里找（流程配方库先）；Host 下一个两个库都没有的新名字，
  看 JSON 的样子由库自己认（`AcceptsSequence` / `AcceptsProcessRecipe`：流程配方每步带 group、工艺配方每步带 values，`Recipe\JsonSteps.AllHave`）。
  S7F1 → PPGNT（0 可以、5 不在 REMOTE）；S7F3 下配方（PPBODY 收 A / B 的 JSON）→ ACKC7（0、1 不让：不在 REMOTE、新名字看不出是哪种、库没过检查，原因记日志）；
  S7F5 → S7F6 PPBODY 用 B（UTF-8），没有回空表；S7F17 删（空表 = 全删；有一个没有就都不删回 4）；S7F19 列（流程配方在前）。下、删要 REMOTE。
  配方变了报 `ProcessProgramChange`（DV `PPChangeName` = 配方名、`PPChangeStatus` 1 建 / 2 改 / 3 删；本地和 Host 改的都报）。
  SC 只有 `LockLocalEditInRemote`（默认 False）：True 时 ON-LINE REMOTE 下 `IsLocalEditLocked` 为真，本地配方服务拒改。以后要前缀再加在这个组件的 SC 里。
- **E87**（照老 CTC 写，定法见 decisions.md「EAP 各标准」E87 一条）：挂到每个 LoadPort 上（`IE87Callback` + `IE84Provider`，PortID 按传进来的先后从 1 编）。
  - 文件：`E87Component`（声明 DV / 事件、收设备回调、E84 反查、报事件）、`.Host`（S3 报文）、`E87Port`（一个端口：6 个状态机 + 载具号 + 放行标记 + 建的时候取一次的 `Carrier`）、
    `E87StateMachine`（小基类：转换表、发消息、进状态时带上原来的状态）和 6 个状态机类（搬运、存取方式、关联、载具 ID、槽图、取放，各自的状态枚举数值照 SEMI，没有载具 = 255）。
  - 写法：设备回调、Host 报文只做"翻成消息发给状态机"（`OnPort` 在锁里找端口、做事、最后刷搬运状态）；事件在状态机进状态时报，
    要设备动的（Load、Unload、重新读码、通知 E90）用 `Later` 攒着，出锁再做；写回设备的核对状态、载具号（`Carrier.UpdateStatus` / `SetId`）、片号表在锁里直接写（只拿设备的小锁）。
  - 设备侧的载具（到达、读码、槽图、取放）全在 `ICarrier`：`E87Port` 建的时候取一次存成 `Carrier`，载具 ID、槽图、取放三个状态机构造时接成 `_carrier`，
    别每处都写 `device.Carrier.Xxx`；Host 要重读码走 `Carrier.ReadId()`。载具这一半的上报由 Carrier 经端口交给的入队口发，跟端口自己的上报同一条线。
  - 流程：读到号建对象等 Host（#1、#3），读码失败的 Host 带端口号给号（#1、#4）；ID 认定就 Load；读到槽图一律等 Host（#14）；
    第二次 ProceedWithCarrier 带槽图就比、对不上回 CAACK=3，片号表写晶圆账，槽图认定（#15）通知 E90 建片对象；Load 好就算在取放（#18）；
    干完了 E87 不自己卸（2026-10-09 起自动 Unload 归 LoadPort 的 SC `AutoUnload`，接不接 EAP 都生效），关着的等 Host CarrierRelease；取消、放行的卸好了端口转等取。
  - 搬运状态：`E87TransferStateMachine.Compute` 按 Host 启停用、设备 `LocalTransferState`、放行标记算，`Refresh` 转过去（等送、等取互换中间补挡着）；
    EC `PortPollMs` 定时、每次回调和 Host 动作后都刷；E84 每拍问 `Compute`。
  - S3F17：ProceedWithCarrier、CancelCarrier、CancelCarrierAtPort、CarrierRelease、CarrierReCreate；S3F25：InService / OutOfService / ChangeServiceStatus / ChangeAccess；S3F27。
  - 不支持（回 CAACK=1）：Bind / CancelBind、CarrierNotification / CancelCarrierNotification、端口预约、读写标签（S3F29 / 31）、内部缓冲设备的动作；
    也没有 E39 的 Carrier / Port 对象、端口 SV、夹紧 / 松开事件。
- **E90**：挂在晶圆账上（`IE90Callback`），按账报片的位置（在来源 / 机内 / 回到载具）和工艺（要做 / 在做 / 做完 / 中止 / 没做成 / 跳过）、片位有没有片；
  接了 E87 时 LoadPort 上的片等槽图认定才建片对象。片位号：单槽的位置用模块名，多槽的用"模块名.两位槽号"。没有读片号的设备，片号核对不做。
- **E40 / E94**：Host 的 S16F11 / 15 / 5 / 17 / 19 / 21 / 27、S14F9 翻成 `IJobManager` 的命令（来源 Host），被拒的错误码经 `JobErrors` 翻成 E5；
  建 PJ 的料只收一个载具加槽号（槽表空原样交给 Job 管理 = 料到了取载具上正常的片），料可以还没到（Job 管理先建着等）；料没到时报料报要的槽号；
  不支持配方参数、暂停事件、改回片地方。
  PJ / CJ 的状态转换（`IE40Callback` / `IE94Callback`）报 PrJobSMTrans01~18 / CtrlJobSMTrans01~13。
- 冒烟：`HsmsSmoke`（链路和分发）、`EapSmoke`（各标准对假 Host、假 LoadPort、真晶圆账、假 Job 管理、假配方库）；
  两个真配方库的按名字列、取、存、删和上报在 `SequenceSmoke` / `ProcessRecipeSmoke` 里测。

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
- 组件发 C# 事件（`AlarmChanged`、`ValueChanged`、`WaferManagerComponent.Wafer*`），在 `ServiceExtensions` 里桥成 EventBus 消息。
- 变化很密的（整篮 Mapping）只推"哪里变了"的轻通知，让界面自己攒一下再拉（`WaferLedgerChangedDto`）。
- 周期推送放 `xyz.Service\Events\*Publisher`（静态、吞异常、同一个故障只记一次日志）。

### 主界面用到的几处（2026-10-04）
- 系统设置 `SystemSettingsDto` 除了 Modules、Chambers，还带 `LoadPorts`、`Robots`（装配出来、启用的，先后同 sc.xml）：客户端主界面照它生成 LoadPort 页签和默认调度图。
- 机械手站点 `RobotStationDto.Kind`（`StationKind`：LoadPort / Chamber / Other）：`BaseRobotModule` 跟槽数一样从搬运模块表认
  （`TransferManager.TryGetStation` 拿到的是 `BaseLoadPortModule` / `BaseChamberModule` / 别的），表没绑好或不在表里算 Other；变了算状态变化。
- 设备总状态 `EquipmentStatusDto.IsAuto` = 搬运管理的自动派单开着（`TransferManager.IsAutoDispatch`），`EquipmentStatusPublisher.Snapshot(modules)` 算一次。
- 整机操作 `IEquipmentService`（`xyz.Service\Systems\EquipmentService`）：`AutoAsync` 开自动派单（没配搬运管理回 `transfer.not_installed`，
  停用了回 `transfer.disabled`）、`ManualAsync` 关自动派单（Job 不再派新动作，在途的做完）、`StopAsync`：搬运管理 `Abort()`（关自动调度 + 中止当前搬运操作）；正在执行动作的模块直接发 Abort（闲着的不碰，不等中止做完，Data = 直接发了几个），
  在给 Job 做工艺的腔体（`JobManager.IsJobProcess`）、在搬运的机械手除外（由 Job / 搬运管理收场）；最后 Job 全部走中止（`AbortAllAsync`）——
  要先认出哪些腔体在给 Job 做工艺：Job 的中止当场就给它们发中止，之后就认不出来了。

### 启动顺序（`AddXyzServices`）
日志队列 → `SC.Load` → `ComponentLoader.Load` → EC 合并 + 推送桥 → GEM 编号表 → 报警 / 晶圆账推送桥 → PLC Open + Start →
IO 表 Open → Safety Start → 轴 Open → 各模块 `InitComponent`（连驱动、登记槽位，子组件基类递归，不动硬件）→ **晶圆账开机恢复**（`WaferManagerComponent.Restore`：模块登记完槽位之后、开始扫描之前）→ 各模块 Start →
TransferManager Bind + Start → 流程配方库 Bind + 变更推送桥 → 工艺配方库 Bind + 变更推送桥 →
JobManager Bind + Start（模块、搬运管理、配方库都起来之后）→ **EAP Bind**（各标准接到 LoadPort、晶圆账、Job 管理上，最后开 HSMS 链路）→ 设备总状态 / IO 推送 →
数据曲线采样、实时曲线推送 → 注册 gRPC 服务。宿主在这之后才起 Kestrel（HTTP/2，地址取 sc.xml `Rpc` 节点，默认 localhost:5000）。
新组件要连接、登记的，写在自己的 `InitComponent` 里（挂在模块下的子组件跟着基类递归走，模块里不用点名）；要在装配层单独 Open/Start 的根组件（PLC、IO 这类），
按依赖放进这个顺序（读点表的排在 IO 表之后，用 PLC 的排在 PLC 之后）。

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
  `Restore()`（启动顺序里模块 InitComponent 之后、Start 之前）：腔体、机械手上的放回去（同一个内部标识，流水记 `Restored`），**LoadPort 上的不恢复，以开机 Mapping 为准**，
  重启前在加工的记成中止，放不回去的（位置没装、越界、槽上有片）丢掉记告警。宿主退出（`ApplicationStopping`）调 `StopSnapshot()` 最后存一次。
- Job 记录：`control_job` / `process_job` 一个 Job 一行，跟着进度更新（见 §3 Job）。
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
