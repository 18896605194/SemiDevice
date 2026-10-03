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
Service\xyz.Components 组件（→ Configs、Database、Drivers、Secs；**不引用 xyz.Shared**）
Service\xyz.Modules   模块（→ Components、Drivers、Shared）
Service\xyz.Service   gRPC 服务实现 + 装配 + 事件桥（→ Shared、Tools、Database、Modules、Configs）
Service\xyz.GrpcHost  宿主（WinExe，托盘图标，单实例；→ Shared、Service）
```

- 组件层看不到契约层，所以"组件事件 → EventBus 推客户端"的桥都搭在 `xyz.Service\ServiceExtensions.cs`。
- 平台不引用机型工程；机型 DLL 由 DeployToHost 拷到宿主 `Modules\<机型>\`，装配时按目录扫描。
- 没有单元测试工程，测试是 `D:\Code\tools\*Smoke` 控制台程序（见 machine-and-tools.md）。

## 2. 组件（`xyz.Components\ComponentBase.cs`）

- 属性：`Name`、`FullPath`（"Chamber1.Door"，纯分组节点不进路径）、`InitOrder`（默认 10000）、`Children` / `AddChild`；
  查找 `FindChild(name)`、`FindChild<T>(name)`、`FindChild<T>()`、`FindChildren<T>()`。
- 扫描：根组件（模块、PLC、Safety、TransferManager）调 `Start()` 起一条长任务循环：`OnScan()` → 慢扫描检查 → `Thread.Sleep(50)`。
  `protected virtual void OnScan()` 会递归子组件，重写时先调 `base.OnScan()`。
- 生命周期：`Init()`（先子后己、按 InitOrder，开机不自动调）、`Abort()`（只停，不清报警）、`Reset()`（先子，再清本组件报警）。
  模块把返回类型收窄成 `ModuleOperation?`。`Open()` 不在基类，各类型自己定义（模块、PLC、IO、HSMS、驱动、轴）。
- 配置钩子：`OnSettingLoaded(ModuleConfig)`——[SCEditor] 灌完值后调，配置不对就抛异常（开机直接报出来）。
- 单例：`public static X? Current { get; set; }` + 构造里 `Current = this;`（报警、EC、System、Log、Rpc、WaferManager、Io、Safety、Hsms、
  DataChart、RealChart、PLC、TransferManager、GemCollectors、SequenceComponent）。用的地方 `X.Current` 先取到变量再判空，没装就降级不崩。

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
  事件声明 `[EventAttribut("FOUP 到达")] public readonly string FoupArrivedEvent = "FoupArrived";`（类名少个 e，是历史拼写）。

### 目录（xyz.Components）
- 顶层只有 `Attributes` / `Collectors` / `Components` / `Enums` / `Interfaces` / `Models` + `ComponentBase.cs`、`ComponentLoader.cs`。
  顶层目录 = 命名空间（`xyz.Components.Models` 等）。
- `Components\` 下按类别分 System / Plc / Actuators（气缸、阀、喷嘴、灯）/ Sensors / Motion / Drivers（品牌驱动壳）/ Charts / Eap，
  **命名空间一律 `xyz.Components.Components`**（子目录只归类）。
- 纯数据类进 `Models`（一个类一个文件），枚举进 `Enums`；只给某个组件用的内部类跟着组件放。不建按领域分的顶层目录。

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
  `BaseLoadPortModule`（子组件按类型找 Driver / RFID / E84；Open 里先开 RFID、E84，再登记晶圆账槽位、开驱动）、
  `BaseRobotModule`（sc.xml 子节点 `Stations` 读站点表：Number、Y、Direction、Arms；`Pick/Place(arm, 站点名, slot)` 成功后改晶圆账）、
  `BaseChamberModule`（Open 只登记晶圆账）。
- 腔体部件（`BaseChamberModule`）：按 sc 结构认——名叫 `Door` 的气缸、腔体下名字以 `Bowl` 开头的第一个气缸（sc 里叫 Bowl1）、
  第一个 `SpinMotor`、每个 `ArmAxis`（它下面第一个气缸是 Lift，喷嘴按 sc 先后）。`ChamberPartsDto` 跟 `ChamberDto` 同 token（模块名）、
  类型不同，各推各的；气缸给指令侧（看线圈，`TwoStateComponent.IsOpenCommanded`）和在走（`IsTraveling`），
  摆臂给 `Reach`（0 = 回零的 0 位，1 = EC Center）和 `EdgeReach`（Edge / Center，示教过才有，界面分两段画），轴位置变化在到位容差内不推。
  `ArmAxisComponent` 示教位：`Edge` = 配方 0（第一个边缘）、`Center` = 配方 150（晶圆中心）的实际轴位置，默认 0 / 150。部件手动动作 `TryPartAction(路径, ChamberPartAction)` → `ChamberService.PartActionAsync`：
  迁移表 `Manual`（未初始化 / 空闲 / 报错可发，执行中 `ChamberState.Manual` 120，做完回原来的状态）；指令在发起线程锁内发，
  发不出去回 `chamber.part_command_rejected` 不改状态、不报警；`ChamberPartOperation` 只看部件自己的 ActionState，EC `PartActionTimeout` 兜底。
  旋转电机"转"用 EC `ManualSpeed`。联锁还没做。
- 状态推送：`PublishState()` 里 `EventBus.Send(dto, Name)`（token = 模块名，留存），只在变化时发（`dto.HasStateChanged(上一次)`）。
  能放片的模块在推送里带上晶圆账（`LedgerSlots = WaferLedgerSnapshot.SlotsOf(Name)`，腔体是 `Slots`），并在 HasStateChanged 里比较——
  账一变下一拍就推给所有界面，不另发通知；界面画片以账为准（见 decisions.md）。
- 流程配方库 `Recipe\SequenceComponent`（sc.xml `Sequence` 节点，SC：`Capacity` 99 个编号、`Folder` `Recipe\Sequence`、`NameMaxLength` 32）：
  编号 1~Capacity，一个编号一个文件（`001.xml`，`SequenceData`，先写 `.tmp` 再整个替换）；坏文件、编号越界的文件跳过并记日志。
  可选站点分组由 `Bind(settings, modules)` 生成（模块全起来后调）：sc.xml 顶层没 Type 的分组节点下、机械手 `Stations` 到得了的站点模块，
  顶层直接装的站点自成一组；第 1 步和最后一步必须是 LoadPort 组（组里全是 LoadPort），有腔体的组每步要填工艺配方；名字一律照 sc.xml 原样。
  保存带版本号（对不上回 `sequence.revision_mismatch`，防两个人同时改），至少 3 步；`Changed(编号)` 在锁外发。服务 `ISequenceService`（`xyz.Service\Recipes`）。

## 4. 驱动（`xyz.Components\Components\Drivers` + `xyz.Drivers`）

- 品牌驱动壳：抽象基类（`RobotDriverComponent`、`LoadPortDriverComponent`、`RfidDriverComponent`）管 SC 通讯配置、驱动生命周期、`DeviceEvent`
  和触发方法；品牌壳（`RejeRobotComponent`、`FcdLoadPortComponent`、`FcdRfidComponent`）只实现 `CreateDriver()` 和命令工厂。换品牌 = 改 sc.xml 的 Type。
- `xyz.Drivers`：`ICommunication`（串口 / TCP，`CommunicationFactory`）、`IFrameCodec` + `FrameCommunication`（收包泵 + 发送锁）、
  品牌协议放 `Robot\Reje\`、`Loadport\FCD\`、`Rfid\FCD\`（Protocol、FrameCodec、Commands）。驱动回调只改状态，动作由模块扫描线程推进。
- HSMS（`xyz.Secs` + `HsmsComponent`）：设备端被动、独占绑定、单会话；S9 只由设备发；`PrimaryReceived` 在收包线程，别在里面同步等 SendAsync。

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
  - 全局事件 DTO 带 `public const string EventToken`（"Alarm"、"Ec"、"EquipmentStatus"、"Io"、"Log"、"RealChart"、"Sequence"、"WaferLedger"）；
  - 模块状态 DTO 用模块名做 token、**留存**（客户端订上立即拿到当前值）；同一模块再推一种 DTO（腔体的 `ChamberPartsDto`）也用模块名，类型不同互不覆盖；
  - "发生了一件事"类用 `retain: false`。
- 组件发 C# 事件（`AlarmChanged`、`ValueChanged`、`WaferManager.Wafer*`），在 `ServiceExtensions` 里桥成 EventBus 消息。
- 变化很密的（整篮 Mapping）只推"哪里变了"的轻通知，让界面自己攒一下再拉（`WaferLedgerChangedDto`）。
- 周期推送放 `xyz.Service\Events\*Publisher`（静态、吞异常、同一个故障只记一次日志）。

### 启动顺序（`AddXyzServices`）
日志队列 → `SC.Load` → `ComponentLoader.Load` → EC 合并 + 推送桥 → GEM 编号表 → 报警 / 晶圆账推送桥 → HSMS Open → PLC Open + Start →
IO 表 Open → Safety Start → 轴 Open → 各模块 Open → 各模块 Start → TransferManager Bind + Start → 流程配方库 Bind + 变更推送桥 → 设备总状态 / IO 推送 →
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

## 8. 配置文件

- 源：`Service\xyz.Configs\Config\sc.xml`（只有这一个；UTF-8 BOM、Tab 缩进、节点带中文注释）。编译时拷到宿主 `bin\...\net10.0\Config\sc.xml`（**运行时读的是这份**）。
- 启动时在同目录生成：`ec.xml`、`EcDefinitions.xml`、`SvDefinitions.xml`、`AlarmDefinitions.xml`、`EventDefinitions.xml`、`DvDefinitions.xml`；
  IO 点表 `Config\IO\*.csv` 来自机型工程。
- 节点顺序：System → Rpc → Hsms → EC → Database → Alarm → Log → WaferManager → Plc → Io → Safety → DataChart → RealChart →
  LoadPort → Robot → Transfer → Sequence → Chamber → …（平台组件在前，模块在后）。加组件时把全部 Value 和注释写进 sc.xml。
- 外部工具 ScEdit（`D:\tools\ScEdit`）按 `[Component]` / `[SCEditor]` 编辑 sc.xml，备份到 `Config\backup\`（已 gitignore）。

## 9. 日志

- `LogHelper.Debug/Info/Warn/Error(module, message)`（module 一般用组件 `Name` / `FullPath`），写 `Log\xyz-日期.log` 和控制台；
  Warn 以上进 `LogQueue` 推给客户端顶栏。中文、说清楚是什么、为什么、接下来会怎样（如"人工调整记录写库失败（账已经改了）"）。

## 10. 线程和错误处理

- 共享状态：`private readonly object _gate = new(); lock (_gate)`；**事件在锁外发**；给调用方的是副本/快照。
- 单读者 `Channel<T>` 做后台写库、EAP 回调；`Interlocked` 计数；`volatile` 快照替换。
- 配置错、装配错：抛 `InvalidOperationException`（带节点名和值），开机就暴露。运行期：返回 bool / null / 结果枚举 / 操作的 Code+Args，
  服务层翻成错误码；循环和计时器里 catch 住记日志，不让设备线程崩。
- 解析用 `CultureInfo.InvariantCulture`；计时用 `Stopwatch` / `Environment.TickCount64`。
