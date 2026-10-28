# 后端

路径相对 `xyz.Core`。.NET 10（`net10.0`），Nullable、ImplicitUsings 全开；仓库里没有 `Directory.Build.props`，各工程也没设 RuntimeIdentifier / PlatformTarget，输出在 `bin\<配置>\<框架>\`（路径里不带 RID）。

## 1. 工程和依赖方向

```
Shared\xyz.Tools      EventBus、跨进程事件的信封和黑板、JsonHelper、XmlHelper、IocHelper、ExcelHelper（不引用任何工程）；Mapster 包只在这儿引用，前后端经 xyz.Shared 都带上
                      子目录只归类、命名空间都是 xyz.Tools：Events\（进程内 EventBus、跨进程的信封 EventMessage 和黑板 RetainedBoard、订阅凭据 Subscription）、
                      Excel\（ExcelHelper 和它的列、表）、Helpers\（IocHelper、JsonHelper、XmlHelper）；根目录只放工程文件
Shared\xyz.Shared     契约：Services\I*Service、Dtos\、Errors\ErrorCodes、Rpc\（→ Tools）
Service\xyz.Common    日志：LogHelper（后端记日志一律走它）、ILogSource（两层名字）、LogQueue、LogFileReader、NLog.config；
                      帮助类在 Helpers\（只归类，命名空间还是 xyz.Common）：DateTimeHelper（本机时间 ↔ UTC 毫秒）、ValueHelper（数据类型、值的换算）；
                      日志在 Log\（命名空间 xyz.Common.Log）
                      跨进程事件的发送方在 Events\（RemoteEventPublisher，命名空间 xyz.Common.Events；→ Tools）
Service\xyz.Configs   machine.xml（读 machine.xml）、配置模型、Paths.json（→ Common、Tools）；machine.xml 本身归机型（如 xyz.35021.Module\Config\machine.xml）
Service\xyz.Database  SqlSugar：XyzDb、实体、分表
Service\xyz.Drivers   纯协议/通讯（不引用 Elements）
Libs\xyz.Secs.dll     跟主机、厂里系统通讯的底层（不在本仓库：源码是单独的 git 仓库 xyz.Secs（GitHub 18896605194/Secs），编 Release 自动拷来，见 machine-and-tools.md §1）：
                      SecsII / Hsms（SECS-II、HSMS）、Gem（HsmsLink 链路分发 + E30 / E39 / E40 / E87 / E90 / E94 / 配方 / Trace 的协议）、Eda（Interface A）
Service\xyz.Elements 元件 + 模块动作 ModuleRoutine + 设备侧给 EAP 的接口（→ Configs、Database、Drivers、Shared、Libs\xyz.Secs.dll；
                      引 DLL 时 NuGet 不知道它依赖谁，元件层照 xyz.Secs.csproj 自己列 ASP.NET Core 框架 + CoreWCF + WCF 客户端包，改版本两边一起改）
Service\xyz.Modules   模块（→ Elements、Drivers、Shared）
Service\xyz.Service   gRPC 服务实现 + 开机流程（只做框架的事）（→ Shared、Tools、Database、Modules、Configs）
Service\xyz.GrpcHost  宿主（WinExe，托盘图标，单实例；→ Shared、Service）
```

- 元件层引用 xyz.Shared：错误码、EAP 接口用到的 DTO、元件自己推给界面的 DTO（2026-10-05 起；原来不引用）。**推送是元件自己的事**
  （2026-10-18 起）：元件在自己的扫描或钩子里 `RemoteEventPublisher.Send`，转 DTO 走映射元件，启动代码里不搭桥（见 §5「事件推送」）。
  元件层的内部成员只对 xyz.Modules 开放（`InternalsVisibleTo`）：模块基类要控制
  ModuleRoutine"什么时候算真正做完"（`DeferCompletion` / `NotifyCompletion`）；所以 xyz.Modules 里重写 `protected internal` 的成员
  （`OnSettingLoaded`）要写 `protected internal override`，机型工程照旧 `protected override`。
- 平台不引用机型工程；机型 DLL 由 DeployToHost 拷到宿主 `Modules\<机型>\`，装配时按目录扫描。
- 没有单元测试工程，测试是仓库根目录 `tools\*Smoke` 控制台程序（见 machine-and-tools.md）。

## 2. 元件（`xyz.Elements\ElementBase.cs`）

- 属性：`Name`、`FullPath`（"Chamber.Chamber1.Door"：machine.xml 的分组节点不生成元件，但名字进路径）、`InitOrder`（默认 10000）、`Children` / `AddChild`；
  查找 `FindChild(name)`、`FindChild<T>(name)`、`FindChild<T>()`、`FindChildren<T>()`。
- 扫描：根元件（模块、PLC、IO（等 PLC 连上下发开机 DO）、Safety、Computer、指挥层 Dispatch）调 `Start()` 起一条长任务循环：`OnScan()` → 慢扫描检查 → `Thread.Sleep(10)`（`ElementBase.ScanIntervalMs`，2026-10-22 用户从 50 改成 20、又改成 10）。
  `protected virtual void OnScan()` 会递归子元件，重写时先调 `base.OnScan()`。子元件之间要互不耽误的父元件重写 `OnScan`、逐个调 `ScanChild(子元件)`：
  每个子元件的异常、慢扫描记在它自己名下，一个抛异常不会让这一拍排在后面的漏扫（基类的直接递归会）。现在只有 `DispatchElement` 这么用。
  **子元件要每拍做事就重写 `OnScan()`，跟着大元件那一条线程走**（LoadPort1 下的驱动、读头、载具，Chamber1 下的气缸、电机、DI 都是这样）：
  只有大元件开机时调一次 `Start()`，子元件不调 `Start()`、不自己起线程或定时器做每拍的事；`OnScan()` 里不等待（不 Sleep、不同步等回复），
  要等的（连网口、写库、发报文）交给后台任务，扫描线程每拍只看结果（如驱动重连）。驱动收数据、后台写库这类 IO 线程不算扫描，回调里只记状态、下一拍由扫描线程处理。
- 生命周期：`InitElement()`（**不动硬件**的开机初始化：连接、登记晶圆账、挂事件、输出回安全态；先子后己、按 InitOrder 递归到每一层子元件，
  一个子元件没做成不耽误别的、汇总返回 `bool`；开机由 `ElementLoader.InitAll` 对每个根元件按 InitOrder 调一次（PLC 100、IO 200 排最前），
  子元件跟着基类走，**父元件不点名**，元件有事就重写、记得调 base）、
  `Abort()`（只停，不清报警）、`Reset()`（先子，再清本元件报警）；Abort / Reset 模块把返回类型收窄成 `HandleResult<ModuleRoutine>`（跟别的动作入口一样，被拒带原因）。
  **要动硬件的初始化（回原点）是模块的 `InitModule()`**（`BaseModule`，返回 `HandleResult<ModuleRoutine>`，人或调度才调，开机不调，不递归子元件；
  LoadPort / Robot / Chamber 基类里 = `Home()`，机型要多做别的步骤就重写）。元件初始化里不放会动轴、动气缸的事——部件怎么回零、什么先后，
  写在所在模块的 Home 操作里去驱动。`BaseModule.Open()` 已经没有了；PLC、IO 在自己的 `InitElement` 里连，轴在自己的 `InitElement` 里
  把收发块登记到 PLC 上（装起来的树上两根轴配了同一个命令块就抛），HSMS 由 EAP 在 `OnStarted` 里开；
  气缸 / 阀的 `Open()` / `Close()` 是开合动作，不是生命周期。
- 开机后面几个阶段（2026-10-18 起，元件自己的事都挂在这儿，启动代码不点名）：`Bind()`（元件初始化都做完、开始扫描之前，整棵树按 machine.xml 先后
  先父后子挨个调；按接口找别的元件、认装起来的模块、订阅、开机恢复，如晶圆账恢复、配方库认腔体、EAP 接各标准）→ 开始扫描 →
  `OnStarted()`（都在扫了以后，如 EAP 开链路）→ `PublishOnStartup()`（开机推送，2026-10-28：界面一连上（含重连）就要拿到的推一次、留存——
  配方库、流程配方库、账号、晶圆账推一条"变了"，日志设置、AI 状态、实时曲线和数据曲线的信号表推内容；一个出错记日志、不耽误后面的）→ 宿主退出时先停扫描（`StopScan`：等手上这一拍扫完，最多等 2 秒）、再 `OnStopping()`
  （倒着来，一个出错记日志、不耽误后面的；EAP 断链路、数据曲线落库、断 PLC、晶圆账最后存一次——这时扫描已经停了，不会跟扫描撞）。
  都是 `ElementBase` 的空虚方法，要用就重写。
- 配置钩子：`OnSettingLoaded(ModuleConfig)`——[Machine] 灌完值后调，配置不对就抛异常（开机直接报出来）。
- **元件都在 IoC 容器里，找元件一律按接口、从 `IocHelper` 取**（2026-10-18 用户定的）：装配器照 machine.xml 树把每个元件按它实现的 xyz 接口
  （命名空间 xyz 开头的）登记成单例（`ElementLoader.Register(services, roots)`，宿主开机装配那一步；树本身也登记成 `IReadOnlyList<ElementBase>`）。
  取一个用 `IocHelper.GetService<IXxx>()`，先取到变量再判空，没装就是 null、降级不崩（`IAlarm`、`IEc`、`ISystemModule`、`ILogElement`、
  `IRpc`、`IMapper`、`ILedgerManager`、`IIo`、`IPlcIo`、`IPlcBlock`、`ISafety`、`IEapElement`、`IHsms`、`IE30Element`、
  `IDataChart`、`IRealChart`、`ITransferManager`、`IJobManager`、`ISequenceElement`、`IRecipeElement`……；接口名原来都带 Element 后缀，用户在一个个去掉：
  `IAi`、`IIo`、`IAlarm`、`IEc`、`IDataChart`、`IRealChart`、`IHsms`、`ILedgerManager`、`IMapper`、`IRpc`、`ILight`、`ISafety`、`IWaferSize` 和设备元件的公共接口 `IDevice`
  已经不带了（2026-10-20、10-21），见 decisions.md「接口去掉 Element 后缀」；
  EAP 那边只认配方库的，用 `ISequenceLibrary` / `IRecipeLibrary`）；同一个接口登记了好几个的，取一个拿最后登记的。
  要一类的全部用 `IocHelper.GetServices<IXxx>()`：按 machine.xml 先后（先父后子），停用的连同下面的跳过。GEM 编号表 `GemCollectors` 不是元件，也登记在容器里，一样从 `IocHelper` 取。
  不写 `Xxx.Current`、不在构造里登记、不在元件基类上另包取法，不按基类、具体类找，不往 Bind 里传整棵树。
- **元件里用到的单例开机时取一次、存成字段**（2026-10-18 用户："你怎么能每一个方法里面都获取呢"）：在 `Bind` 里取；开机准备里就要用的在 `InitElement` 里取
  （晶圆账取映射元件、IO 元件取 PLC、E84 取 IO：模块开机准备时就要建片记流水、经 IO 写 DO）；被父元件在它 Bind 里接上的标准件（E30、E87、Trace）在自己的 `Attach` 里取。
  有 `Bind(modules)` 重载的（整机、Job、搬运管理、两个配方库）在重载里取，冒烟直接调重载也取得到。自己建出来、不在树上的元件（Job 没配 Scheduler 时建的那个）
  由建它的替它 `Bind`。元件基类的报警、EC、报事件三个公共方法每次现取（开机每个阶段、所有元件都在用）。模块动作（`ModuleRoutine`）的步骤要用的，模块建动作时直接用自己的字段（步骤多是模块里的 lambda）。

### 装配（`ElementLoader`）
- 类上 `[Element(description: "中文说明")]`，machine.xml 的 `Type` 写**类型全名**（如 `xyz.Elements.Elements.CylinderElement`、
  `xyz._35021.Module.Loadport.LoadPortModule`）——所以元件挪目录不能改命名空间。需要 public 无参构造。
- 扫运行目录 `*.dll` + `Modules\**\*.dll`；找不到 Type 直接抛异常（宿主托盘变红）。
- 没写 Type 的节点：父元件已有同名子元件就灌值给它，否则是纯分组（LoadPort、Database、Chamber），不生成元件、名字照样进 FullPath（Chamber.Chamber1）。
- 灌值：先把所有可写 [Machine] 属性设成默认值，再按 `<Value Name>`（不分大小写）覆盖；支持 int / double / bool / string / enum（InvariantCulture），转不了抛异常带节点名和值。
  **名字对不上的（元件上没有这个 [Machine] 属性）记一条错误日志（模块名 = 节点全路径）、照常开机，那一项还是默认值**（2026-10-24 用户定：不抛）；
  不写 Type、由元件自己读的数据节点（工艺配方的字段表 `RecipeField`）认名字的地方一样记错误日志。

### 参数：machine.xml 和 EC（代码里不留魔法数）
- **machine.xml**（装机、接线、结构性的，改了重启生效）：`[Machine("默认值", "分组", "说明")]`，C# 初始值和默认值写成一样：
  ```csharp
  [Machine("25", "LoadPort", "花篮槽数")]
  public int SlotCount { get; set; } = 25;
  ```
  IO 下标默认 `"-1"`（没接）。开关一律 bool（`IsEnable`），不用 High/Low 这类字符串。
- **EC**（现场在线调的：超时、防抖、周期、批量，改了下一拍生效）：
  ```csharp
  [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "10000", "200", "报警防抖时间")]
  public int DebounceMs { get { return GetEcInt(nameof(DebounceMs)); } set { SetEcInt(nameof(DebounceMs), value); } }
  ```
  键是"元件全路径.属性名"。ec.xml 不随源码，后端启动时 `EcElement.Merge` 按元件树生成：文件没有、或者是空的（清空了），照元件树整份建；
  有内容才往里合并（只补新项、不动已有值，元件树上没有了的旧项留着不列）。想清掉旧项就把文件清空或删掉、重起宿主；界面在 设置 → EC 设置 改，
  后端校验格式/范围后写回并推送。
- machine.xml 一般只由 ToolBuilder 改、重启生效；**运行时要改、改完一直生效的**（现在只有晶圆尺寸 `WaferSizeElement.Size`，主界面切换）用 `MachineXml.SaveValue(元件全路径, 参数名, 值)`
  写回宿主读的那份 machine.xml：只改那一个 Value（没有就照缩进补一行），别的内容、缩进、换行（LF / CRLF 照原文件）、BOM 原样，先写临时文件再换上去。
- 协议、结构常量用 `const`。新元件先分好 `#region Machine` / `#region EC` 再写逻辑。

### 报警
```csharp
[Alarm("开/关到位超时", AlarmCategory.IrrecoverableError, AlarmLevel = AlarmLevel.Light, Description = "...", Solution = "...")]
public string TimeoutAlarm = nameof(TimeoutAlarm);
```
- **分类就是 SEMI E5 ALCD 的类别**（2026-10-22）：按出了什么事选——伤人 1、坏设备 2、工艺量预警 3 / 超限 4、设备或动作出错要人处理 5、
  设备状况要留意 6、要人来看一下 7、账或 Mapping 对不上 8；值原样进 S5F1 / S5F6 / S5F8 的 ALCD 低 7 位。轻重另看 `AlarmLevel`，两个不互相推。
- **等级 `AlarmLevel`**（2026-10-27 改名，原来叫 Alarm1 / Alarm2）：`Warn` 警告、`Light` 轻报警、`Heavy` 重报警、`Fatal` 致命，值 1 ~ 4 从轻到重；
  `[Alarm]` 不写等级默认 `Heavy`。现在 Warn 亮黄灯、不生成报出 / 清除事件，别的都亮红灯；EDA 里 Light / Heavy 都算 Error。停不停机、挡不挡开工以后统一设计。
  等级是按名字走的（历史库、`AlarmDefinitions.xml` 存名字，界面等级颜色、历史页等级筛选按名字比），改名要连这些字符串一起改。
- 报：每拍条件型 `CheckAlarm(code, condition, debounceMs)`；一次性 `RaiseAlarm(code)`。不用事先注册，第一次报时反射取定义。
- **报警只能人工复位清**（`AlarmElement.Reset(sourcePath)` → 元件 `Reset()`），源头恢复、动作成功都不自动清。
- 历史入库（按天分表），报出、清除各一行。客户端推送由 Service 层桥接 `AlarmChanged`。
- SECS 用的 SV / EC / ALID / CEID / DV 编号表由 `GemCollectors.Merge` 按代码声明生成（`*Definitions.xml`，号段固定、删掉的停用保号）；
  事件声明 `[EventAttribut("FOUP 到达")] public readonly string FoupArrivedEvent = "FoupArrived";`（类名少个 e，是历史拼写），
  事件带的数据（DV）先声明 `[DataVariable(ValueFormat.String, "载具号")] public readonly string CarrierIdData = "CarrierID";`，
  事件上写 `Data = new[] { "CarrierID" }`（同一元件上的 DV 代码）；报事件 `RaiseEvent(FoupArrivedEvent, new GemData("CarrierID", id))`——
  发给容器里全部收事件的元件（`IEventReceiver`，2026-10-25 起；原来只点名 E30）：E30 发 S6F11（值当场取好、排进发送线程），EDA 的 E134 按采集计划采；
  不阻塞（扫描线程上也能调）；没接 EAP、离线、Host 关了这个事件时 E30 什么都不做，EDA 没有计划要它时也不做。
  **EAP 各标准元件自己的事件（PJ / CJ / 载具 / 片的转换、配方变了，E30 的控制状态、缓存、操作员改常量）也走 `RaiseEvent`**，不直接调 E30，EDA 才收得到
  （xyz.Secs 的引擎经 `IGemEvents` 交回元件，元件 `RaiseEvent`）。
  SV 属性可以直接返回 `SecsItem`（格式要精确的，比如列表），也可以是 byte / ushort / uint（报 U1 / U2 / U4），int 照老规矩非负报 U4。

### 目录（xyz.Elements）
- 顶层只有 `Attributes` / `Collectors` / `Elements` / `Enums` / `Interfaces` / `Models` + `ElementBase.cs`、`ElementLoader.cs`。
  顶层目录 = 命名空间（`xyz.Elements.Models` 等）。
- `Elements\` 下按类别分 System / Plc（PLC：抽象底座 `PlcElement` 只管连接、重连、掉线报警，品牌子类 `BeckhoffPlcElement`（ADS，`IPlcIo` + `IPlcBlock`）、`OpcUaPlcElement`（OPC UA，只 `IPlcIo`）管读什么、对外给哪个接口，见 decisions.md「PLC 底座只留连接、OPC UA PLC」；IO（`IIo.WriteDo` / `WriteAo` 回 `HandleResult` 带原因，联锁、AO 超标定范围都在 IO 元件里拦，谁写都一样，见 decisions.md「IO 写入带原因」）、心跳 `HeartbeatElement`：按 machine.xml 的时间轮流写一个 DO，见 decisions.md「心跳」；虚拟 IO `VirtualIoElement`：下面挂虚拟 DO，程序按点号读写，见 decisions.md「虚拟 IO」）/ Actuators（气缸、阀、阀组、喷嘴、灯）/ Sensors / Motion / Drivers（设备通道：串口 `SerialDriverElement`、网口 `TcpDriverElement`，共用基类 `CommDriverElement`；读头底座 `RfidReaderElement`、重连 `DriverReconnector`；品牌在机型工程）/ Charts / Eap / Eda（Interface A，见 §4「EDA」）/ Cds（供液：槽 `TankElement`，见 decisions.md「CDS 供液：槽 Tank」）/
  Maintenance（部件寿命：次数型 `CountLifetimeElement` 挂在部件下面盯 DO 计次、时间型 `TimeLifetimeElement` 默认从清零起按实际时间算，配了计时 DO 只算它通着的、配了清零 DO 它一变就清零重算，见 decisions.md「部件寿命」）/ Ai（AI 助手 `AiElement`，见 §5「AI 助手」）/
  2026-10-27 从模块层挪下来的 Recipe（工艺配方库、流程配方库）/ Process（开工计数）/ Job（Job 管理，`Scheduling` 调度、`Task` 任务元件）/ Transfer（搬运管理和搬运动作）/ Dispatch / E84 / Loadport（载具元件）/
  StateMachines（状态机底座），映射元件在 System 下（见 decisions.md「xyz.Modules 只放模块」），
  **命名空间一律 `xyz.Elements.Elements`**（子目录只归类）。
- 纯数据类进 `Models`（一个类一个文件），枚举进 `Enums`；只给某个元件用的辅助类单独成文件、跟着元件放在同一目录（不嵌在别的类里，见 decisions.md「不写内部类」）。不建按领域分的顶层目录。
- 模块动作 `ModuleRoutine` 和步骤（`RoutineStep`、平台给的几种步骤、等待扩展 `Wait`）在 `Elements\Routines`（命名空间 `xyz.Elements.Elements`），
  `RoutineState` 在 `Enums`、`DeviceOrder` 在 `Models`——2026-10-05 从模块层挪下来（当时叫 ModuleOperation），好让设备侧接口放进元件层。
- `Interfaces` 下有元件自己的（PLC 分点位 `IPlcIo`（IO 元件用）和数据块 `IPlcBlock`（轴用），由品牌子类自己声明支持哪个；2026-10-18 起每个单例元件都有接口：`IAlarm`、`IEc`、`IMapper`、
  `IDataChart`、`IE30Element`……（Element 后缀在一个个去掉，见 §2），找元件一律按它们）、模块共有的 `IModule`（名字、状态、在做的动作、InitModule / Reset / Abort；生产 / 维护是单独的能力接口 `IMode`，见 §3；
  `ILoadPortAccess`、`ITransferRobot` 继承它；模块自己的接口 `ILoadPort`、`IRobot`、`IChamber`、`IAligner`、`IBuffer`、`ISupplyModule`、`ISupplyAccess`、`ISystemModule` 跟着模块放在 xyz.Modules
  （2026-10-27 用户定，见 decisions.md「xyz.Modules 只放模块」），元件层要整机的经 `ISystemAccess`（界面语言，`ISystemModule` 继承它）），
  还有设备侧给 EAP 的命令接口和上报口：`ILoadPortAccess`（LoadPort 给 EAP 用的那一面，完整的 `ILoadPort : ILoadPortAccess, IJobAccess` 在模块层）、`ICarrier`（经 `ILoadPortAccess._carrier` 拿到，一个端口一个载具元件）、`ICarrierIdReader`（载具读码器，读头底座 `RfidReaderElement` 实现，品牌读头类在机型工程）、`IE87Callback`、`IE84Callback`、
  `IE84Provider`、`IJobManager`、`IE40Callback`、`IE94Callback`、`IE90Callback`（挂在晶圆账 `LedgerManagerElement.E90Callback` 上）；它们用到的 `E84Timer`、`LoadPortTransferState`、CJ / PJ 的状态和命令在 `Enums`，
  载具快照 `CarrierInfo` 在 `Models`（建 CJ / PJ 的方法直接收参数，没有请求类，见 §3 Job）；命令结果用 xyz.Shared 的 `HandleResult`
  （失败时 `ErrorMessage` 放错误码、`Args` 放参数）。
  实现：端口是模块层的 `BaseLoadPortModule`，载具 `CarrierElement`、Job 管理 `JobManager` 10-27 起在元件层；EAP 元件写在元件层，直接用这些接口。

## 3. 模块（`xyz.Modules`）

- **只放模块**（2026-10-27 用户定）：模块本体、模块自己的接口、动作步骤、状态表、状态码；别的元件（配方库、Job、搬运、调度、映射、E84、载具……）
  和它们的接口、数据、枚举都在 xyz.Elements（§2 目录），见 decisions.md「xyz.Modules 只放模块」。
- **目录**（2026-10-09，用户："该归类就是归类用文件夹"）：模块的域目录（`Loadport`、`Clean`、`Robot`……）里，本体（`Base*Module` 和它自己的接口）留在根，其余按类别放子文件夹
  `Enums\`、`Models\`、`Routines\`、`StateMachines\`；**只归类，命名空间不改**（一般 `xyz.Modules`，状态码和动作枚举 `xyz.Modules.Enums`，状态表 `xyz.Modules.StateMachines`；
  状态码的底 `ModuleState` 和状态机底座 `BaseStateMachine` 10-27 起在元件层）。
  `Loadport` 已分好：`Enums\`（PodPresenceSource、LoadPortState 加 LoadPortAction；SlotPickOrder 在元件层 `xyz.Elements\Enums`）、`StateMachines\`（LoadPortStateTable）；
  设备动作统一用元件层的 `ModuleRoutine`（2026-10-23；原来 LoadPort / 机械手 / 对准器各一个 `XxxCommandOperation`，合成 `CommandOperation` 以后同一天又换成一串步骤，见下面「动作 = ModuleRoutine」）；
  `Robot` 也已分（`Enums\`：RobotState 加 RobotAction；`StateMachines\`：RobotStateTable；`BaseRobotModule`、`IRobot` 契约留根；
  示教点是元件层的 `Elements\Robot\TeachPointElement`，接口 `ITeachPoint`，2026-10-24 起替掉原来的 `Models\RobotStation`）；
  `Clean` 也已分（`Enums\`、`Routines\`、`StateMachines\`）；E84 10-27 挪到了元件层 `Elements\E84`。
- 供液（CDS）：`Cds\BaseCdsModule : BaseModule, ISupplyModule, ISupplyAccess`（一种药液一个模块，下面挂它的槽 `Tanks`；`ISupplyModule` = 不跟机械手交互的模块，
  跟 `ITransferStation` 平级）。机型的药液模块继承它（35021 的 `Cds\IpaModule`，推 `IpaDto`：IPA 的状态 + 下面各槽的 `TankDto`，DTO 放机型的
  xyz.35021.Shared）。2026-10-18 只搭了架子：初始化直接进空闲，手动 / 自动、两槽切换、自动补液这些默认做法以后写在基类里。
  给别的模块用的那一面是 `Supply\ISupplyAccess`（`Name`、`IsReady`，2026-10-20）：`IsReady` 在供液模块自己的扫描里每拍算（`protected virtual CheckReady()`，
  默认 = 状态是空闲 + 有一个槽就绪或正在供液），存 volatile，别的线程只读。腔体按自己的 machine.xml `Supply`（模块名）开机取到它，起工艺前看（见 decisions.md「模块之间访问」）。
- `BaseStateElement : ElementBase`（2026-10-23 从 BaseModule 拆出来，模块和整机都继承它）：`abstract int State`（子类加 `[VariableMark(SV, Int, ...)]`，初值 `ModuleState.NotInit`）、
  动作迁移表（`(状态, 动作)` → 执行中/成功状态）、`Begin(action, routine)`（不允许就返回 null，只有 Abort 能顶替在途动作）、动作检查 `CheckState`、挂一个动作和扫描步进、慢扫描 EC。
  `BaseModule : BaseStateElement, IModule` 只放模块才有的：`Kind`、站点类型 `Type`、`InitModule()`（动硬件的模块初始化，没重写的算被拒 `module.action_rejected`）；默认的 `Reset()` / `Abort()` 子元件跟着做完就回做完的空动作。
- **生产 / 维护模式** `IMode`（`Mode`、`SetProduction()`、`SetMaintenance()`；2026-10-23 原来的上线 / 下线改名、从 IModule 拿出来，见 decisions.md「整机不算模块；上线 / 下线改成生产 / 维护模式」）：
  不继承别的接口，要这个能力的类直接实现——`BaseTransferStationModule`（LoadPort、腔体、buffer、对准器）、`BaseRobotModule` 各写一份（虚方法，只改 Mode、不经设备，开机维护）；
  供液、整机没有。调度、整机推模块表按 `is IMode` 判；LoadPort、腔体、机械手的服务各自写切生产 / 维护：按自己的模块接口找到模块、`as IMode` 调 `SetProduction()` / `SetMaintenance()`（服务基类只放通用的回包和找模块，不放具体业务，2026-10-26 用户定）。
- 状态码（`public const int`，有继承）：`ModuleState` NotInit 10 / Initing 20 / Idle 30 / Aborting 35 / Error 40；
  `TransferModuleState` 50/60/70/80；`LoadPortState` 100~150；`ChamberState` Homing 100 / Processing 110 / Manual 120（部件手动动作中）；`RobotState` 200/210/220。
  同一个码在不同模块意思不同，客户端按模块种类翻（`ModuleStates`）。
- **动作 = ModuleRoutine：按顺序走的一串有名字的步骤**（2026-10-23 换掉 ModuleOperation / CommandOperation，见 decisions.md「动作改成一串步骤（ModuleRoutine）」）：
  ```csharp
  // 平台建动作、机型往里加步骤（AddHomeSteps 这类）；一步 = 进这一步时做一次 + 之后每拍判一次做完没有
  var home = new ModuleRoutine(this, "Home", _driver, () => HomeTimeout);   // this = 日志记在谁名下（ILogSource）；发指令的动作带通道和 EC 超时
  AddHomeSteps(home);                       // 机型：home.Command("回零", () => new RejeHomeCommand());
  return Started(RobotAction.Home, home);   // Begin：挂到模块上，模块的扫描线程每拍推一下

  // 腔体这类不发指令的：带整个动作的上限，步骤用设备、条件、别家动作
  var reset = new ModuleRoutine(this, "Reset").LimitTo(() => ResetTimeout, ErrorCodes.ChamberActionTimeout);
  reset.Await("等轴复位", Axes, ChamberDeviceAction.Reset);
  ```
  - 步骤（`Elements\Routines`）：`Command`（发一条驱动指令等设备回，onSuccess 看回来的结果；回错 `device_failed`、超时 `timeout` [动作名, ms]（先作废那条）、
    发不出去 `command_rejected`）、`Start`（起别家的动作等它收尾，原样带它的错误码；`onFailed` 重载自己判码）、`Do`（当场做一下：检查、记账）、
    `Until`（等条件，上限 0 = 判一次不成立就失败）、`Devices`（几个设备同时发同一个或各自的动作，都做完算完；一个设备都没配就不加这一步）、
    `Await` / `AwaitSent`（只等设备做完，指令别处发了）、`Add(名字, run, check)` 或 `Add(RoutineStep)`（自己写，有字段要记的写成 `RoutineStep` 子类）。
    `UseTimeout` 换后面几条指令的超时，`LimitTo(ms, 错误码)` 整个动作的上限（超了报 [模块名, ms]）。开始推以后不能再加步骤；一个步骤只能加进一个动作。
  - 推进：做完的步骤**当拍接着走下一步**，碰到要等的就返回；全走完算做成。只在挂着它的模块（或搬运管理）的扫描线程里推，不加锁、不等待。
    步骤抛异常自动判 `operation_faulted` [动作名, 异常信息]。
  - 出错：只有正在走的这一步能判失败（`step.Fail(code, reason, args)`），整个动作停在这一步；`Code + ErrorArgs` 给界面，`Reason` 只进日志、开头带"第几步 / 共几步 步骤名"；
    日志在模块名下记"开始：共 n 步（各步名字）""第 n 步做完 x ms""做完 / 失败在第几步、这一步多久、在等什么、前面各步用时"。
    `StepNumber` / `StepCount` / `StepName` / `CurrentStep` 随时可读；服务回包 `BaseService.Failed(routine)` 不止一步的带上 `StepNumber` / `StepCount`，界面 `ErrorText` 在原因前面加"第 n/m 步："。
  - 中止 `AbortByHost(reason)`（别的线程也能调）：直接停下（`action_aborted` [动作名]），先调动作自己的 `OnAborted`（子类收场，比如搬运先给机械手发急停），
    再让正在走的这一步收场（作废在途指令、中止在等的别家动作）；还没进的步骤不收场。失败的收场重写 `OnFailed`。
  - 不用做事、建好就做成的用 `ModuleRoutine.Done(模块名, 动作名)`（站点不用准备、没有设备的初始化），不能挂到模块上。
  - RPC 线程用 `WaitReply(ms)` / `Wait(ms)`（扩展：被拒的 null 回 false、等超时抛 `TimeoutException`）等结果，**不能在扫描线程等**。
    挂在模块上的动作停下后先由模块落状态（`OnRoutineCompleted`），再唤醒等的人（`IsSettled`）。超时时间取模块的 EC 属性，每拍现读。
- **动作入口：状态 → 资源 → 互锁**（2026-10-10 用户定的统一结构；LoadPort、机械手的动作都改完了，腔体 2026-10-20 照着改完，见 decisions.md「腔体照 LoadPort / 机械手的写法改」）：
  手动页、自动任务、EAP 都调模块的**同一个入口**，入口在模块锁里按顺序查三项，哪项不过就带原因（错误码 + 参数）返回、模块不动；都过了才 `Begin`（占住模块、落执行态、发指令）。
  调用方不先查、也不重复查。入口不带"谁发的"参数，模块里不写 `if (来自 EAP)`。
  ```csharp
  public HandleResult<ModuleRoutine> Load()                 // 入口不 virtual：三项检查谁都绕不过
  {
      lock (RoutineGate)
      {
          var rejected = CheckLoad();
          if (rejected is not null)
          {
              return Rejected(rejected);                    // BaseStateElement：原因原样转成入口结果
          }

          return Started(LoadPortAction.Load, CreateLoadRoutine());   // 通过：Begin 占住、落执行态；万一被拒回 module.action_rejected
      }
  }

  private HandleResult? CheckLoad()                          // 只查不动；要给外面问的（E87 回 Host 前）做成 public，如 CheckUnload
  {
      return CheckState(LoadPortAction.Load)                 // ① 状态：BaseModule 写好
          ?? CheckLoadResource()                             // ② 资源：模块里写
          ?? LoadInterlock();                                // ③ 互锁：protected virtual，平台默认，机型重写先调 base
  }

  protected virtual ModuleRoutine CreateLoadRoutine() { ... }   // 平台搭好步骤（开门 + 扫片落账），机型填 AddLoadSteps；整个做法不一样才重写这个
  ```
  每项检查返回 `HandleResult?`：null = 过，不过 = 错误码 + 参数（一种原因一个错误码，中英文语言包各一条）。三项怎么分：
  - **状态**（只看自己）：`BaseModule.CheckState(action)` = `CheckAvailable()`（默认看 `CanBeginAction` → `module.disabled`；有驱动的模块重写加"没连上" → `module.not_connected`；
    **Abort 只查停用、不查连没连上**——没连上也要能把在途动作顶掉）→ 状态表没这一行 `module.state_not_allowed` [模块, 状态码] → 有在途动作 `module.busy`（Abort 不查）。跟 `Begin` 查的是同一套，只是带原因。
  - **复位、中止**重写元件基类的 `Reset()` / `Abort()`（子元件递归、报警复位照旧），返回值收窄成 `HandleResult<ModuleRoutine>`，写法跟别的入口一样：
    只查状态（恢复动作不能被资源、互锁卡住），不过 `Rejected(原因)`、过了 `Started(...)`；整机停止、PJ 中止要等设备停下的取 `.Result`（被拒是 null）。
    不另开 `CheckReset` / `CheckAbort` 问原因（2026-10-26 删掉的）。
  - **资源**（这个动作要独占的东西有没有被别人占）：直接问晶圆账、`TransferManager.IsSlotLocked`、`JobManager.OwnerOf`，不另建占用表。
  - **互锁**（周围条件、传感器、别的模块、搬运车）：平台给默认，机型的光幕之类重写、先调 base，用机型自己的错误码。
  - 只跟"谁发的"有关的检查（EAP 要 ON-LINE REMOTE、E87 状态机、Host 认定了载具号才 Load）留在发命令那一层（E87 回 Host 的 CAACK 也在那层翻）；
    判断法：换成操作员在手动页点同一个按钮，这条还该不该拦——该拦放模块入口，不该放发起方。"接了 EAP 才拦、但谁发都拦"（槽图要 Host 认定才让机械手进）看的是设备状态，放模块。
  - 服务层被拒照原样回入口的码和参数（`BaseService.RunRoutine(started, timeout)` 只有这一个，所有模块共用），发起了没做成回 `Failed(routine)`（错误码 + 参数，不止一步的带停在第几步）；
    界面用 `ErrorText.Of(response, ModuleStates.XxxText)`（Presentation 公共的），状态码翻成状态字、带了步号的在前面加"第 n/m 步："（语言包 `routine.step_at`）。
  - 加一个动作照这个顺序：状态表加行 → 资源检查 → 互锁虚方法 → 错误码 + 两个语言包 → 入口照上面写 → 服务只翻结果 → 冒烟每种拒绝原因一项检查。
  **LoadPort 各动作**（入口都在 `BaseLoadPortModule` 的"动作入口"区，`Begin(LoadPortAction)` 只记动作和出发状态、不再查联锁）：

  | 动作 | 资源 | 互锁（平台默认） | 机型填的步骤 |
  |---|---|---|---|
  | `Load()` | 账上的片不归没结束的 Job（`loadport.wafers_in_job` [模块, PJ]，Load 会清账重建、片的内部标识全换新）；有片的槽没被搬运占着（`transfer.slot_locked`） | 载具到位（`loadport.carrier_not_arrived`）；E84 交接没在走（`IE84.IsHandoffRunning` → `loadport.handoff_running`） | `AddLoadSteps`（开门；扫片落账平台加） |
  | `Unload()`（自动口径）/ `UnloadManually()`；只查：`CheckUnload()` | 关门的资源（`CheckCloseDoorResource`）：取出去的片都回来了（`loadport.wafers_outside` [模块, 片数]）、片不归没结束的 Job | 不拦 | `AddUnloadSteps`（关门；要对账平台先加扫片） |
  | `Home()`（`InitModule` = Home，被拒的原因原样回给全部初始化；门开着、从这个口取出去的片还在机内时 `InitModule` 不发 Home、回做完的空操作，全部初始化当它好了，回片要用这个口） | **只在 Loaded（门开着）时**查关门的资源；没初始化 / 出错 / 空闲出发不查——拦住就卡死（端口回不到 Loaded、片也回不来） | 不拦 | `AddHomeSteps` |
  | `Clamp()` / `Unclamp()` | 不碰片和账，不查 | 不拦（现在没有调用方，手动页没按钮） | `AddClampSteps` / `AddUnclampSteps` |
  | `Reset()` / `Abort()` | 不查 | 不设 | `AddResetSteps` / `AddAbortSteps` |

  EAP：E87 取消 / 放行回 Host 之前先问 `ILoadPortAccess.CheckUnload()`（Load 着的；在动作中的照旧回忙），不能卸回 CAACK=2 带端口给的原因；
  `LoadLater` / `UnloadLater` 被拒带原因记日志。Job 干完自动卸（`CheckCarrierComplete`）照旧先等片回齐再调 `Unload()`，被拒带原因记日志。
- 动作失败（被人中止顶掉的不算）在操作终结时由 `RaiseActionFailedAlarm`（`protected virtual`，机型可重写：自己要管的情况先判、报了就 return，其余交给 base，整套换掉不调 base；在模块锁里，只报警不等待）报警，一次失败只报一条：机械手、腔体报 `ControlledStopAlarm`；LoadPort 超时按动作报各自的超时报警（Load / Unload / Home=初始化 / 夹紧 / 松开 / 复位 / 中止，跟 EC 各动作超时一一对应），不是超时的报 `ControlledStopAlarm`（显示为"LoadPort 动作失败"）。设备报错每拍 `RaiseAlarm(XxxDeviceAlarm)`。
- 站点类：`BaseTransferStationModule`（SlotCount、传片环 PrepareTransfer → PrepareTransfer2 → Transferring →（同一站先取后放时取完 PrepareNextLeg 再 Transferring）→ TransferComplete；
  公共入口在锁里查本轮上下文、状态、有没有没收尾的动作，站点只重写 `*Core` 扩展点；machine.xml `HandoffOnPick` / `HandoffOnPlace`（默认 False）开了的站点，
  机械手伸到位停下后调 `HandoffPick` / `HandoffPlace` 中途交接（比如降、升顶针），站点重写 `HandoffPickCore` / `HandoffPlaceCore`，默认被拒，见 decisions.md「分段取放」；
  在位传感器：元件初始化认下面叫 `WaferSensor` 的 DI（`IDiSensor`），`CheckWaferSensors()`（`ITransferStation` 上）machine.xml 配了才跟晶圆账比——看到有片 = 账上这个站有片，
  读不到也算对不上（`station.wafer_sensor_*` [站点]），没配回 null；传感器配法不一样的站点重写它。全部回片开始前用，见 decisions.md「全部回片」）、
  `BaseLoadPortModule`（通道找直接挂的、启用的那条（`CommDriverElement.FindIn`），RFID / E84 按接口找；`InitElement` 里先登记晶圆账槽位、把机型给的解码器交给通道（`UseCodec`）、
  挂通道的上报（`OnReport`），再由基类把子元件（通道、读头、E84）各自初始化——
  连接、E84 输出回初始写在它们自己的 `InitElement` 里，端口不点名；RFID、通道这一次没连上也照样往下走，
  返回 false 只为开机日志看得到，之后由通道元件按间隔重连；`InitModule()` = `Home()`；**状态查询的节奏在平台**（`StatusQueryLoop`）：一圈查什么由机型 `QueryDataLoop` 给
  （35021 一圈一条 GET:STATE，回来写 `Status`），按 EC `QueryStatusIntervalMs`（默认 1000 ms）开下一圈，超过 EC `QueryDataTimeOut` 没回就作废这一条、Status 清空、接着查（机械手同样：超时、断线把伺服、速度、报错、各轴坐标清空，2026-10-26 用户定"跟 LoadPort 一样"），
  超时 / 恢复各记一次日志；**载具交给子元件 `CarrierElement`**（machine.xml 每个 LoadPort 下必配一个 `Carrier` 节点，缺了开机抛 `InvalidOperationException`；`ILoadPort.Carrier` 是它的 `ICarrier` 口，读码、槽图认定状态、取放状态、E87 载具上报都在它里面，见 decisions.md「LoadPort 的载具收进 CarrierElement」）：**在位二选一**（Carrier 的 machine.xml `PresenceSource`，默认 Query）：Query 看状态查询的在位、到位两位（端口每拍用 `Carrier.Sense` 喂进去），
  都亮放好、都灭拿走、一亮一灭或查不到不算变化，Event 看 PODON / PODOF（端口的 `SetDeviceReportedPlaced` 转给 `Carrier.SetDeviceReportedPlaced`，机型有别的上报路子也调它），只在扫描线程判边沿，
  判出来的叫 `Carrier.IsArrived`（载具到了，后台全用它；界面三维图的盒子不看它、跟原始两位走；状态查询的原始位叫 `IsPresent` / `IsPlaced`，`LoadPortStatus` 的开关量一律 `Is` 开头）；动作没做成（失败、超时、被顶替）在 `OnRoutineCompleted` 里把通道的在途指令全部作废；Idle 一律当"门关好、没 Load"，门不确定落 NotInit：
状态表 Reset / Abort 写最保守的（出错 / 没初始化复位、Loaded 复位、中止除 Idle / Error 外一律 NotInit），`Begin` 记下动作前的状态，做成后
`SetStateByDoor` 对动作前门没在动的（Loaded、交互环）按状态查询改：门开且载具在 → Loaded，门关 → Idle，查不到 → 保持 NotInit；
Load 走"状态 → 资源 → 互锁"入口（见上面"动作入口"，`LoadInterlock()` 返回 `HandleResult?`，默认要 `Carrier.IsArrived`、E84 交接没在走，机型有别的条件重写、先调 base；入口不 virtual，整个做法不一样才重写 `CreateLoadRoutine`）；**动作的流程在平台、指令在机型**（2026-10-23，见 decisions.md「品牌驱动：平台只给通道」）：平台用 `ModuleRoutine` 的步骤（`Command` 发指令等做完、`Start` 起一段别家动作（搬运管理排的机械手扫片）、`Do` 当场检查、落账）搭好 Load / Unload / 扫片，每段发哪几条由机型填：`AddLoadSteps`（开门）、`AddUnloadSteps`（关门）、`AddMapSteps`（自己扫片，拿到槽图连同设备回的原文调 `SaveMapResult(槽图, 原文)`）；**Load 和扫片分开**（2026-10-22，见 decisions.md「扫片：LoadPort 和机械手都能扫（IWaferMap）」）：Load 先只开门（35021 发 `CLOAD`，回执带图也不用），再扫片——有机械手开了 `MapEnabled`、示教点表里有这个口，就经搬运管理排它扫，不然自己扫（35021 发 `MAPDO` 扫、`GET:MAPDT` 取槽图）——拿到的槽图交 `TakeSlotMap` 落账（转给 `Carrier.UpdateSlotMap`）；LoadPort 也实现 `IWaferMap`：`Map(自己的名字)` 只在 Loaded 时用自己的传感器单独扫一遍（状态 160 扫片中），结果存进 `MapResults`、不落账；动作做成 / 失败时端口再告诉 Carrier：`StartAccess` / `EndAccess` / `MarkAccessStopped`；Unload 分两个口径：自动跑货（Job 干完自动卸、E87 放行 / 取消）走 `Unload()`，machine.xml `AutoRunMapOnUnload` 开着时**关门前**先扫片（自己扫或叫机械手扫）跟晶圆账对，对得上才发 `CULOD`，对不上判失败、门不关；手动页面走 `UnloadManually()`，一律不扫图、不对账，machine.xml 只管自动跑货用不用它），每拍最后 `PublishState`；机型类必须填 `CreateCodec`、`AddLoadSteps` / `AddUnloadSteps` / `AddMapSteps`、
`AddHome/Clamp/Unclamp/Reset/AbortSteps`（一般一条指令一个 `Command` 步骤），可选 `QueryDataLoop`、`OnReport`（主动上报：放上 / 拿走交 `SetDeviceReportedPlaced`），
整个动作做法不一样的还能重写 `CreateLoadRoutine` 这些（动作入口不 virtual，见"动作入口"）；
  E84 子元件 machine.xml `IsEnable`=False（本机没接搬运车）时 `E84` 属性为 null，端口当没有 E84：不初始化、不每拍推、不读写 IO
  （基类递归会带着 E84 一起初始化，所以 E84 自己的 `InitElement` 也拦 `IsEnable`）——
  跟 EC `E84Enabled`（装了以后现场在线开关交接）分开）、
  `BaseRobotModule`（轴与手指是 machine.xml 节点：X/Z/Theta 用 `RobotAxisElement`、Arm 用 `RobotArmElement`（模块按接口 `IRobotAxis` / `IRobotArm` 用；节点名即轴名，machine.xml `Number` 是手指号，`Position` 是查询回来的轴坐标，`SensorHasWafer` 挂了 `WaferSensor`（DI，按 `IDiSensor` 认）按它、没挂是设备推送的在位；`LedgerHasWafer` 是晶圆账上这只手有没有片（模块初始化时 `SetRobot(Name)`）；挂了的手取放前后、Home 前跟晶圆账对，没挂不查，见 decisions.md「机械手轴与手指做成元件」；全部回片开始前 `IRobot.CheckWaferSensors()` 把挂了的手都对一遍，读不到也算对不上）；设备状态轮询的节奏（`StatusQueryLoop`，扫描线程，跟 LoadPort 一样同一时间一条在途；一圈里各条一条回来接着发下一条，新的一圈要离上一圈开始满 EC `QueryStatusIntervalMs`（默认 1000 ms）才开；超时调通道 `Abandon` 作废那一条、查到的伺服 / 速度 / 报错 / 各轴坐标清空（跟 LoadPort 一样，2026-10-26）；断线这一圈作废、同样清空、调 `OnDisconnected`）和每拍状态发布在平台；一圈查什么、什么顺序、回来写到哪由机型从上往下写在 `QueryDataLoop` 里（`yield return` 一条 = 发出去等它回来；35021 锐洁：没订上先订手指在位推送、报错、伺服、速度、每根轴的坐标，machine.xml `WaferEventEnabled` 关掉不订阅，断线清订阅标志下一圈重订），主动推送机型 `OnReport` 认（手指在位交 `NoteArmPresence`、报错写 `DeviceError`）；机型必须填 `CreateCodec` 和 `AddHome/Pick/Place/PowerOn/PowerOff/Reset/AbortSteps`（一般一条指令一个 `Command` 步骤；取放后面"等持片反馈对上""记账"两步平台加）；示教点表（2026-10-24，见 decisions.md「机械手示教点」）：机械手下 `Size12` / `Size8` 两个分组（一种晶圆尺寸一组，整机现在几寸用哪张，没配的那种尺寸是空表、到不了），下面一个站点一个示教点元件 `TeachPointElement`（节点名 = 站点名；machine.xml Number、Y、Direction、PickArms / PlaceArms：从这个站取片 / 往这个站放片许用的手，不配 = 所有手；配了这台机械手没有的手开机就抛，见 decisions.md「手臂取放分开」）；机械手元件初始化时按 `ITeachPoint` 收子元件、按挂在哪一组分成两张表（挂在两组以外、同组重名开机抛，一个都没有记错误日志），对外 `TeachPoints` / `TeachPointsOf(size)` / `TryGetTeachPoint`；推送的示教点表（`TeachPointDto`）还带槽数、站点类型 Kind（照抄模块自己报的 `IModule.Kind`，机械手不判断具体类型）；`Pick/Place(arm, 站点名, slot)` 成功后改晶圆账；
  分段取放（可选能力 `ITransferRobotStep : ITransferRobot`）的四个方法 `ExtendPick / RetractPick / ExtendPlace / RetractPlace` 也写在这里，但基类不声明接口：
  能分段的机械手类在类上声明它、重写 `AddExtend/RetractPick/PlaceSteps` 加设备那几条（默认一条没有 → `robot.handoff_unsupported`）；伸臂做完停在
  `PickExtended / PlaceExtended`（状态表只收对应的缩臂和急停），伸臂不动账，缩臂只收同一只手同一槽、做完照常查持片记账；
  扫片（可选能力 `IWaferMap`，2026-10-22）的 `Map / MapResults` 也写在基类、不声明接口：能扫的机械手类在类上声明 `IWaferMap`、重写 `AddMapSteps(map, 站点, 站号)`
  （拿到槽图连同设备回的原文调 `SaveMapResult(站点, 槽图, 原文)`；一条没加就是不能扫，`robot.map_unsupported`）；machine.xml `MapEnabled` 开了，它示教点表里的 LoadPort 由它扫（扫片用示教点的 `Number`，用哪只手是控制器示教的事），类没声明接口开机抛；
  入口查空闲、开关、站点、手上全空（`robot.map_unsupported`、`robot.station_not_found`、`robot.arm_has_wafer`），扫片中状态 230）、
  `BaseChamberModule`（`InitElement` 照 machine.xml 认一遍腔体里装的设备存起来（停用的也认），启用的再登记晶圆账，子元件照基类递归；`InitModule()` = `Home()`；**回零、复位、中止、工艺平台给默认实现**（2026-10-10，跟 LoadPort 一样收进平台）；
  2026-10-20 照 LoadPort / 机械手的写法改完：动作入口 `Home()` / `StartProcess` / 手动设备动作回 `HandleResult<ModuleRoutine>`，入口不 virtual，
  机型类留空、只在设备做法不一样时重写 `AddHomeSteps` / `AddResetSteps` / `AddAbortSteps` / `AddPrepareTransferSteps` / `CreateProcessRoutine`，
  要加条件重写 `HomeInterlock` / `ProcessInterlock` / `DeviceActionInterlock`（先调 base）；工艺动作 `ChamberProcessRoutine`（`Clean\Routines`）是 public，机型能照它写自己的；每拍最后 `PublishState`）。
- **腔体里装什么照 machine.xml 认、强类型**（2026-10-10 用户："未来其他项目直接修改 machine.xml 文件就行，代码基本不要修改"；原来的 `[PartKind]` / `[LiveValue]` / `[ManualAction]` + 反射 + 字符串字典整套删了，"Part"这个叫法也不用了）：
  machine.xml 里一个腔体下挂门、Bowl、卡盘、摆臂（`SwingArmElement`，它自己就是摆动轴；硬件上装在它上面的升降是一个 `CylinderElement`、喷嘴是几个 `NozzleElement`，都挂在摆臂节点下面）。
  `BaseChamberModule` 在元件初始化时直接从子节点按接口认一次、存起来：`Door`（`IChamberDoor`，只能一个）、`Bowls`（`IChamberBowl`，可多层）、`SpinMotor`（`ISpinMotor`，只能一个）、`Arms`、`FindArm(节点名)`（配方用），
  另有 `Axes` / `Cylinders` / `Nozzles` 全量（手动页按路径找、整腔动作逐个发）；冒烟里手搭的腔体要先调 `InitElement()` 才认得到。摆臂自己给 `Lift`（下面第一个气缸）、`Nozzles`、`FindNozzle(药液)`。没有另外的"部件表"类。
  推送 `ChamberDeviceDataDto`（结构跟 machine.xml 一样：`Door`、`Bowls`、`Spin`（`ChamberAxisDto`，在不在转看 `IsBusy`）、`Arms`（`ChamberArmDto`：摆动轴的状态 + Reach / EdgeReach + `Lift` + `Nozzles`）；
  token 模块名、留存、有变化才推，跟 `ChamberDto` 类型不同互不覆盖；条目是 record，浮点按 3 位取整）。门、Bowl、卡盘、摆臂以外的东西不在推送里（35021 没有，真有了再加一个位置）。
  加新种类的硬件（加热、真空……）：写元件 + 在模块 / DTO / 服务 / 界面各加一处，这是平台一次性的活；之后各项目 machine.xml 里挂就行。
  做没做完由元件自己说：`ElementBase.ActionState`（基类默认已做完，自己管动作到完成的轴、气缸、阀重写；2026-10-07 用户："为什么是腔体判断，不应该"，去掉了 `IActionElement`）。
- 腔体设备手动动作（手动页）：服务是具体方法 `AxisHomeAsync` / `AxisMoveAsync`（位置、速度 0 = EC MoveSpeed）/ `AxisStepAsync`（步距不能 0）/ `AxisJogAsync` / `AxisStopAsync` / `AxisResetAsync`、
  `CylinderUpAsync` / `CylinderDownAsync`（参数装在 `RpcRequest` 里：`Module` + `Device` 路径，轴动作是 `Module` + `Axis` 加位置 / 步距 / 速度），模块上对应 `AxisHome`……`MoveCylinder`
  （回 `HandleResult<ModuleRoutine>`，停止回 `HandleResult`，服务只把被拒的原因原样转回去）：先认设备、查参数，再在锁里查 状态（`CheckState(Manual)`：停用 `module.disabled`、
  状态不允许 `module.state_not_allowed`[模块, 状态码]）→ 互锁（`DeviceActionInterlock`，平台不拦）。
  普通动作走迁移表 `Manual`（未初始化 / 空闲 / 报错可发，执行中 `ChamberState.Manual` 120，做完回原来的状态），指令在锁内当场发，发不出去回 `chamber.device_command_rejected`
  不改状态、不报警，挂的动作只有一步 `AwaitSent`（只看设备的 ActionState，回到 Idle 算被中止），EC `DeviceActionTimeout` 兜底；
  停止不看忙不忙、不挂动作，发出去就回；点动挂两步的动作（"按住"等松手、"等停下"）——按下发一次，一直走到界面松手发停止（中间不发别的、不会自己停，2026-10-26 用户定），
  停下才退出 Manual。错误码：`chamber.device_not_found`（这个路径不是这种设备）、
  `chamber.device_args_invalid`（不是有限数、速度负、点动速度 / 步距 0）；第二个参数是动作名 `ChamberDeviceAction`（Shared 枚举），界面按 `device.action.*` 翻（`DeviceActionText.Of`）。
- 腔体整腔动作（都在 `BaseChamberModule` 的 `AddXxxSteps` 里，多用 `Devices` 步骤：一步里几个设备同时发、都做完再走下一步；指令发不出去 `chamber.device_command_rejected`、
  设备失败 `chamber.device_action_failed`、整个动作超过 EC 超时 `chamber.action_timeout`[模块, ms]（`LimitTo`）；machine.xml 里没配的设备那一步不加）：
  - 回零 `AddHomeSteps`（用户定的先后，一步一个名字）：喷嘴全关 → 卡盘停转 → 各臂 Lift 升 → 摆臂、卡盘（和别的轴）回零 → Bowl 降，门不动（门归站点交互环）。Lift 先升，摆臂回零才不刮 Bowl 壁。
  - 取放准备二 `AddPrepareTransferSteps`（`PrepareTransfer2`，动作名 `ChamberAction.PrepareTransfer2`，上限 EC `DeviceActionTimeout`）：卡盘 `MoveTo` 卡盘 EC `TransferPosition`（取放片示教位）、
    开门，同时发、都到位才进 TransferReady；卡盘先发，没回零（绝对定位要求已回零）就失败、门不开、腔体落 Error。卡盘、门都没配的直接准备好。
  - 复位：轴的驱动器复位在元件基类 `Reset` 递归时就发了，`AddResetSteps` 只等每根轴做完；中止：轴停止同理在元件 `Abort` 里发了，`AddAbortSteps` 把喷嘴全停液（气缸、阀的中止输出保持原样）再等轴停下。
  - 工艺 `ChamberProcessRoutine`（`Clean\Routines`，上限是配方头的超时时间 `RecipeData.TimeoutMilliseconds`，腔体 EC 没有工艺超时了；请求里没带配方快照的 `CheckProcess` 就挡下 `chamber.recipe_steps_missing`）：喷嘴全关、Lift 升、Bowl 升 → 每一步：先关掉这一步不用的喷嘴（卡盘加减速时不让上一步的药液接着喷）→ 转速给卡盘（0 停转、跟上一步一样不重发）→
    别的摆臂回 Home（轴 0 位）、这一步的摆臂摆到"位置"（晶圆坐标，`SwingArmElement.ToAxisPosition` 按示教 Edge / Center 换）→ 有流量设定 AO 的先给流量再开药液对得上的喷嘴 →
    Time 停着喷 / Scan 在位置和"到"之间按"速度"（轴的设计速度，原样当 MoveTo 速度，不按示教换算）来回扫，到点进下一步（扫到一半当场停轴）→ 收尾停液、摆臂回 Home、停转、Bowl 降。
    认的字段名：`Seconds`、`Rpm`、`Arm`、`Chemical`、`Flow`、`Mode`（Time / Scan）、`Position`、`ScanTo`、`ScanSpeed`，字段表里没配、这一步没填的跳过。
    配方选的摆臂 / 药液这个腔体没有 `chamber.recipe_option_missing`；没有配方步骤（没装工艺配方库）`chamber.recipe_steps_missing`。工艺失败、超时（不是中止）`FinishProcess` 先把喷嘴停了。
    腔体 machine.xml `Supply` 配这个腔体用的供液模块名（几个逗号隔开，空 = 不看），`Bind` 时按名字从 `GetServices<ISupplyAccess>()` 取（不分大小写、重复的只取一次，
    配了没有开机抛，停用的腔体不取）；起工艺时有一个没就绪，互锁 `ProcessInterlock` 挡下 `chamber.supply_not_ready`[模块, 配方, 供液模块]；只看开工这一下，喷的中途不再看。
    腔体 EC `SupplyCheckEnabled`（默认 True）关了不看（调试时供液还没调好用），每次起工艺现读、改了当场生效；名字照样开机取、配错照样开机报。
  - 卡盘速度单位 rpm（配方转速原样当 Spin 速度），卡盘 EC `MaxSpeed` 要不小于配方里的转速（轴的通用默认是 100）。转着换转速：上一条就是 Spin 时 PLC 报忙也照收（2026-10-10 仿真器上发现转着改不了转速）。
    喷嘴 machine.xml `AoIndex`（-1 不接）：`SetFlow(L/min)` 写 AO，`Stop()` = 关阀，machine.xml `ClearFlowOnStop`（默认 True）开着的再 `SetFlow(0)`（AO 设定和流量计目标都清零），False 保持上一步的流量。
    流量计 = 喷嘴下面挂的 `AiSensorElement`（machine.xml 节点名 `Flow`，`MonitoringDoIndex` 填喷嘴阀的 DO）：上下限、预警、防抖、报警都用它的，
    EC `MonitoringDelayMs` = 监控 DO 变有效（开阀）后等多久才判；没挂就是这一路没流量计。喷嘴 `SetFlow` 给流量计 `SetTarget`（没 AO 也给），
    流量计配 machine.xml `IsPercentOfTarget = True`：EC `AlarmMin` / `AlarmMax` / `WarnMin` / `WarnMax` 就是目标的百分比（别的 AI 是固定数），
    目标 0 不判、目标变了延时重新计；喷嘴 `FlowMeter` 按接口 `IAiSensor` 认子节点，挂两个开机就报。
  气缸 `TwoStateElement.Position` 三态（命令发到哪侧看哪侧到没到位，没到 = Unknown；两个线圈都没通时只看到位反馈）。
  摆臂 `SwingArmElement.Reach`（0 = 回零的 0 位，1 = EC Center）/ `EdgeReach`（Edge / Center，示教过才有，三维分两段画），
  示教位：边缘 `Edge8` / `Edge12` = 配方 0（第一个边缘）、`Center` = 配方里的晶圆中心（8 寸 100、12 寸 150）的实际轴位置，默认 50 / 0 / 150，
  按整机尺寸（摆臂取一次存的 `IWaferSize`，`WaferSizeElement.SizeOf(...)`）取那一套（`Edge` 是当前尺寸的边缘）；旋转电机在不在转用轴的 `IsBusy`（PLC 报忙，`ISpinMotor` 不另加）。
  轴手动页参数默认值 EC：`MoveSpeed`、`JogSpeed`（点动 / 步进速度）、`JogStep`（步距）。
  轴的"目标已在到位容差里就不发"只用于绝对定位（MoveTo）；相对移动（MoveBy，步进）照发——步距默认 1 跟到位容差默认 1 一样大，
  以前会被当成已经到位、根本不走（2026-10-04 联调时发现）。联锁还没做。
- 状态推送：`PublishState()` 里 `RemoteEventPublisher.Send(dto, Name)`（token = 模块名，留存），只在变化时发（`dto.HasStateChanged(上一次)`）。
  能放片的模块在推送里带上晶圆账（`LedgerSlots = WaferLedgerSnapshot.SlotsOf(Name)`，腔体是 `Slots`），并在 HasStateChanged 里比较——
  账一变下一拍就推给所有界面，不另发通知；界面画片以账为准（见 decisions.md）。
- 流程配方库 `Recipe\SequenceElement`（machine.xml `Sequence` 节点，参数：`Capacity` 99 个编号、`Folder` `Recipe\Sequence`、`NameMaxLength` 32）：
  编号 1~Capacity，一个编号一个文件（`001.xml`，`SequenceData`，先写 `.tmp` 再整个替换）；坏文件、编号越界的文件跳过并记日志。
  可选站点分组由 `Bind(settings, modules)` 生成（模块全起来后调）：machine.xml 顶层没 Type 的分组节点下、机械手示教点表里到得了的站点模块，
  顶层直接装的站点自成一组；第 1 步和最后一步必须是 LoadPort 组（组里全是 LoadPort），有腔体的组每步要填工艺配方；名字一律照 machine.xml 原样。
  保存带版本号（对不上回 `sequence.revision_mismatch`，防两个人同时改）；不限步数、名称不限字符（2026-10-13 用户去掉的，只查必填、长度、不重名）；
  `Changed(编号)` 在锁外发。两个配方库的操作结果都直接用不带泛型的 `HandleResult`（不另造结果类，配方放在 `Result` 里，
  用的地方 `result.Result as SequenceData` 取；EAP 口子直接回同一个结果）。服务 `ISequenceService`（`xyz.Service\Recipes`）。
- 工艺配方库 `Recipe\RecipeElement`（machine.xml `ProcessRecipe` 节点，参数：`Capacity` 99、`Folder` `Recipe\Process`、`NameMaxLength` 32）：
  编号、文件、版本号、`Changed(编号)` 跟流程配方库一样。一份配方 = **配方头 + 步骤，两部分有哪些项都不写死**，按本节点下两张字段表：
  `Head`（配方头，一个子节点一项）、`Fields`（步骤，一个子节点一个字段 = 配方页一列），节点名就是字段名；值：`Text` / `TextEn` 名字、`Type` Int / Double / Choice / Bool / Text、
  `Unit`、`Min` / `Max`、`Decimals`、`Default`、`Required`、下拉的选项。`OnSettingLoaded` 里读（`RecipeField.FromConfig`），配错就抛（字段名格式、重复、
  步骤必须有 `Seconds` 且是 Double、配方头固定的 10 项（`RecipeHead.Fixed`）都要有且类型对、上下限、默认值、下拉没写选项……）。
  配方头：编号、名称、创建 / 修改记录、版本程序算（`RecipeHead.IsAuto`）；`Type`、`Timeout`（超时时间 s）、`Description` 是 `RecipeData` 根上的属性，
  自己加的项在 `<Head …/>` 上（`RecipeData.Head`）。`Type` 的选项是能做工艺的站点（`IProcessStation`）配的几种 Type（`BaseModule.Type`，machine.xml 参数，`Bind(modules)` 时收集，`StationTypes`）。
  步骤下拉的选项按腔体 Type 分开写：`Source.A`（腔体没配 Type 的写 `Source`），`RecipeField.OptionsFor(type)`，按配方头的 Type 取；
  `Bind` 时查每种 Type 都写了、默认值在里面（配错开机就抛），老配方没写 Type 的补第一种，没写超时时间的读文件时补默认值。
  配方对不对得上具体腔体由腔体自己查：`IProcessStation.FindRecipeMismatch(配方)`（`BaseChamberModule` 实现：先比 Type，再跟执行共用
  `ChamberProcessRoutine.FindDevices` 照名字找 `Arm`、`Chemical`）——流程配方保存经 `RecipeElement.FindMismatch(配方名, 腔体名)`
  （`sequence.recipe_option_missing`），Job 挑腔体（`TaskElement`），腔体起工艺（`chamber.recipe_option_missing`）。检查按字段表（步骤用 `process_recipe.value_*`、
  配方头用 `process_recipe.head_value_*` 两组通用错误码，字段名按 System 语言取中文名或英文名），字段之间不互相管；合计时长（`Seconds` 加起来）不超过配方头的超时时间。
  至少 1 步，新建时配方头填默认值（Type 取第一种）、带一步默认值。保存 `Save(编号, 版本, 内容, 操作人)`，内容就是一份 `RecipeData`（只看配方头能填的项和步骤）。
  文件：一步一个 `Step`，每个字段写成一个属性（`XmlAnyAttribute`），**字段表里的字段都写、空的也写**——读的时候没有这个属性就是字段表后来加的，
  按默认值补；字段表里没有的属性先留着、下次保存丢掉。存之前规整写法（整数、小数去多余写法，开关小写，下拉按选项的大小写）。
  按名字被引用：流程配方查配方在不在库里（`sequence.recipe_not_found`），腔体起工艺查（`chamber.recipe_not_found`）；库没装都不查。
  锁的先后：`SequenceElement` 锁里可以调 `RecipeElement.Contains` / `FindMismatch`，反过来不行。服务 `IProcessRecipeService`（`xyz.Service\Recipes`），
  `GetOptionsAsync` 给两张字段表（配方头标出程序算的、Type 带腔体配的几种；步骤带按 Type 分开的下拉选项）。两个库的 `Find(名字)` 给的是克隆（配方快照，库里再改不影响）。
  **Host 远程管配方**：流程配方库实现 `Interfaces\ISequenceLibrary`：`SequenceNames`、`GetSequenceData`（导出）、`AcceptsSequence`、`ImportSequence`、`DeleteSequence`、`E30Callback`；
  工艺配方库实现 `Interfaces\IRecipeLibrary`：`RecipeNames`、`GetRecipe`（导出）、`AcceptsRecipe`、`ImportRecipe`、`DeleteRecipe`、`E30Callback`。
  元件层 `Interfaces` 下的 `ISequenceElement` / `IRecipeElement`（服务和腔体用；两个库在 `Elements\Recipe`，10-27 从模块层挪下来）各自继承这两个。
  导出按名字取库里的副本转 JSON（工艺配方步骤是字段名 → 值，`RecipeStep.Attributes` 是存 XML 用的、JSON 里不带）；导入按名字新建或覆盖配方头能填的项和步骤（没带 Type 的不收，没带超时时间的补默认值），
  检查、规整跟本地一样，JSON 里的编号 / 版本 / 人和时间不管；满了 `*.full`、读不出来 `*.body_invalid`，删除时没有名字回 `*.name_not_found`。
  两个 `Accepts` 方法只看 JSON 的样子，Host 下新名字时分库用。
  上报口 `E30Callback`（`IE30Callback`）：分别调用 `SequenceChanged` / `ProcessRecipeChanged`；建、改、删、改名（旧名删 + 新名建）都在 `Report` 里经 `EapNotifierElement` 报。
  配方服务改配方（建、改名、存、删）之前先问 EAP 的配方管理（构造里取的 `IE30RecipeElement`）的 `IsLocalEditLocked`，锁着回 `recipe.locked_by_host`。
  腔体按工艺配方的步骤执行（2026-10-10）见上面"腔体整腔动作"：执行按字段名认（Seconds、Rpm、Arm、Chemical、Flow、Mode、Position、ScanTo、ScanSpeed），流量下发到喷嘴的 `AoIndex`。
- **buffer** `Buffer\BaseBufferModule : BaseTransferStationModule, IBuffer`（跟 LoadPort、腔体一样：自己的能力走 `IBuffer`，跟机械手交互走站点基类），
  机型写空类 `BufferModule : BaseBufferModule`：两台机械手之间放片的架子上的一段，只做取放、不做工艺。machine.xml 里 `Buffer` 是分组节点（不写 Type），
  下面进片段、出片段各配一个（machine.xml `SlotCount` 几层、`IsEnable`），各自 `IsRun`。开机在晶圆账登记槽位、待命、自己切生产模式
  （没有手动页）；全部初始化时直接回待命。跟 JK 一样，机械手控制器里两段各教一个站号，示教点表里各配各的 `Number`，槽号原样发、不换算，
  机械手、搬运管理、调度都不认识 buffer。见 decisions.md「buffer」。
- **对准器** `Aligner\BaseAlignerModule : BaseTransferStationModule, IAligner`（2026-10-22；机型写一个类继承、填品牌指令）：一个槽，`Kind` = `StationKind.Aligner`，只声明取放
  （流程配方这一步不选工艺配方，任务表只有放片、取片）。下面必挂一条启用的通道（串口 `SerialDriverElement` 或网口 `TcpDriverElement`），没有开机抛；还没有品牌。
  开机登记晶圆账一个槽、自己切生产模式（没有手动页），`InitModule()` = `Home()`。
  **片放上来自己对准**：`OnTransferFinished` 里这一轮放了片就接着 `Begin(Align)`（`TransferComplete → Aligning → Idle`），搬运照常收尾、机械手不等；
  起不了（驱动断了、互锁 `AlignInterlock` 不让）落 Error、报 `ControlledStopAlarm`。**台上的片没对好不让取**：`CheckPrepareTransfer` 加一条 `aligner.wafer_not_aligned`
  （记的是对好的那片的内部标识，跟账上台上那片比，只在内存）。回零时台上有片串一条对准（`CreateHomeRoutine(hasWafer)`）。
  动作用 `ModuleRoutine`：回零 = 机型的 `AddHomeSteps`，台上有片再加 `AddAlignSteps`（`UseTimeout` 换成对准的超时）；对准 = `AddAlignSteps(align, EC NotchAngle)`；
  失败作废在途指令、报警（人为急停顶掉的不报）。machine.xml `IsEnable`；EC `NotchAngle`（度，默认 0）、`HomeTimeout` / `AlignTimeout`（30 s）、`ResetTimeout` / `AbortTimeout`（10 s）。
  机型必须填：`CreateCodec`、`AddHomeSteps`、`AddAlignSteps`、`AddResetSteps`、`AddAbortSteps`；可选 `OnReport`、`HomeInterlock` / `AlignInterlock`、`RaiseActionFailedAlarm`，
  整个动作不一样再重写 `CreateHomeRoutine(hasWafer)` / `CreateAlignRoutine`。见 decisions.md「对准器」。
- **加工口** `Process\IProcessStation`（继承 `IModule`，带 `Type`；`IChamber` 继承它，`BaseChamberModule` 实现）：手动起工艺和 Job 走同一个口子。
  工艺配方（Type 选项、配方对不对得上站点）、流程配方（哪些分组要选配方）、Job 都认 `IProcessStation`；`IChamber` 只放腔体手动页的东西
  （整腔回零 / 复位 / 中止的超时、轴和气缸手动、腔体推送），腔体手动页、调度图和账单页的"腔体"分组认它。以后热板、冷板这类不是腔体的加工站点只实现 `IProcessStation`。
  `HandleResult? CheckProcess(ProcessRequest)` 只问不动设备，跟别的动作入口一样 状态 → 资源 → 互锁（状态：停用 `module.disabled`、状态不允许 / 在忙 `module.state_not_allowed` / `module.busy` →
  资源：没配方 → 配方对不上这个腔体 → 槽号 / 片号对不上 `chamber.wafer_mismatch` → 互锁：配的供液没就绪 `chamber.supply_not_ready`；2026-10-20 起先查状态，腔体忙时先报忙）；
  `HandleResult<ModuleRoutine> StartProcess` 在同一把锁里查完就 Begin，被拒原样带原因，起了把账上的片标成 InProcess，做完（`OnRoutineCompleted`）标 Completed / Failed / Aborted。
  站内任务 `CheckTask` / `StartTask` 转给这两个（`StartTask` 被拒回 null，调度下一拍再问）。平台默认按配方走 `ChamberProcessRoutine`，机型做法不一样才重写 `CreateProcessRoutine(ProcessRequest)`（检查过了才调，请求里一定带配方快照）。
  "腔里的片归 Job 时手动不能起"（`chamber.wafer_owned`）只拦手动、不拦 Job 自己，留在服务层 `ChamberService.ProcessAsync`。
  `ChamberService.ProcessAsync` 先取库里的配方快照，腔里的片归某个 Job 时拒（`chamber.wafer_owned`）。
  开工计数和进度也在这个接口上（调度、界面只读，见 decisions.md「工艺站开工计数、进度和并行站轮着用」）：`TodayWafers` / `TotalWafers`（每片开工加 1，
  中止、失败也算；共用的 `Elements\Process\ProcessCounter`（元件层）按天一站一行存默认库 `process_daily_count`，`Bind` 时读回来接着数）、
  `LastStartedTimestamp`（Stopwatch 开机以来的计时，只用来比先后、只在内存；不用系统时间，Host 对时、改时间往回调先后不乱）、
  `ProcessPercent`（0~100，只在内存，没在做工艺为空；平台的 `ChamberProcessRoutine` 按配方每步时间算，准备段 0、收尾段 99，机型自己造的工艺动作为空）。
  以后的热板、浸泡槽实现接口时用同一个 `ProcessCounter`。
- **指挥层** `Dispatch\DispatchElement`（machine.xml 顶层 `Dispatch`，下面先挂 `Job`、再挂 `Transfer`）：Job 派单、搬运管理推工单共用它这一条扫描线程，
  一拍里按 machine.xml 先后逐个 `ScanChild`（先收结果、派单，新派的单当拍就走第一步），彼此之间没有线程交叉；工单（TransferRoutine、SwapRoutine）没有自己的线程，跟着搬运管理走。
  设备（机械手、LoadPort、腔体）不挂这里，各自在根上一条线程，指挥层经设备的动作入口下令（设备锁里检查完就挂上动作）、看 `IsSettled` 收结果。决定见 decisions.md「指挥层 Dispatch」。
- **搬运管理** `Transfer\TransferManager`（machine.xml `Dispatch.Transfer`，扫描由 Dispatch 带着）：手动、Job、人工恢复共用的唯一执行口，来源 `TransferOrigin` Manual / Auto / Recovery。
  `Start(TransferRequest)` 当场回 `HandleResult<TransferRoutine>`（成功给实际设备操作，拒绝给错误码 + 参数）；请求只是参数，不建立搬运单、单号、回执或结果历史。
  启动时查：启用、晶圆账、站点、槽号、同槽、源片标识、目标槽空闲、Job 归属（人工恢复 Recovery 可搬 Job 的片）、资源占用、机械手可达与手臂可用。
  调度直接查搬运管理的 `IsSlotLocked` / `IsRobotInTransfer`，手臂占用在搬运启动时内部校验，不另建占用快照类。
  源槽、目标槽、片、手一起占用；机械手忙或站点与正在执行的操作重叠就拒绝，不另排搬运队列。自动任务仍在原任务表等待下一拍。
  执行都派生自 `TransferExecution`（抢站点、两步准备、换段、落交互标记并发取放、失败挂起、中止）：`TransferRoutine` 一片一趟，`SwapRoutine` 同腔交换（见下）。
  `TransferRoutine` **先抢目标站点再抢源**，子操作 `IsSettled`（模块收完尾、记完账）才继续；每片的进度记在 `Wafers[i].Progress`（None / Picked / Placed，
  `TransferRoutine.HasPicked` 是它的简写），整趟资源收尾及设备中止确认后才置自身 `IsSettled`。
  站点交接上下文 `TransferContext` 分取、放两段（`ForPick` / `ForPlace` / `ForPickThenPlace`），站点按对象身份认本轮；同一站先取后放（同站换槽、交换）
  取完不收尾，调 `PrepareNextLeg` 换段（基类默认空操作，站点要在两段之间动设备就重写 `PrepareNextLegCore`）再放，整轮占着站点。
  **分段取放**：站点开了 `HandoffOnPick` / `HandoffOnPlace` 的，`BeginMotion` 要求机械手是 `ITransferRobotStep`（不是就落标记前拒，`robot.handoff_unsupported`），
  先调 `ExtendPick / ExtendPlace`，伸臂做完叫站点 `HandoffPick / HandoffPlace`，交接做完调 `RetractPick / RetractPlace`，缩臂收尾才算这一下取放做完；
  交接被拒 / 没做成，机械手不缩臂、就地中止，整趟失败挂起（`transfer.station_handoff_rejected` / `failed` [站点]）。
  `Bind` 时查：开了交接的站点，到得了它的启用机械手都要声明 `ITransferRobotStep`，不然抛。
  每趟的占用是一份 `TransferClaims`（槽、手、片、站点），占锁、放锁、挂起都照它。
  没动手就失败：把抢到的环撤回待命态（`ITransferStation.CancelTransfer`）、释放资源；**动过手才失败：保留资源、站点停在交互中**（`HeldRoutines`、`NeedsRecovery`）。
  人工确认片位、对好账后 `ReleaseHold(晶圆内部标识)`（交换挂起的一条记录按其中任意一片都能整条放开）。`Cancel(操作)` / `CancelOwner` / `CancelAll` 请求由搬运扫描线程执行中止；
  `CancelOwner(Job)` 只中止这个 Job 还有片没走完的（`HasPendingWork`），`HasUnfinishedTransfer(Job)` 查它还有没有在跑或挂起的搬运（Job 中止收尾用）；`Abort()` = 关自动调度 + 全部中止。
  **同腔交换** `StartSwap(SwapRequest)` → `HandleResult<SwapRoutine>`：同一站同一槽，取片手先取走预期的旧片（取片手要空）、放片手再放进预期的新片，两只手不同、
  都要在这站许用；一次占齐这一槽和两只手，旧片标 `EndsOnArm`（取完就算走完，下一步由调度另派）。决定见 decisions.md「同腔交换、预取」。
  **源可以是机械手**（片已经在手上：重启前搬到一半、手动取了没放）：`Source` 写机械手名、`SourceSlot` 写手指号，就用拿着它的那只手，只放片
  （`TransferRoutine` 抢目标 → 准备二 → 放；`Source` 为 null、`SourceName` 是机械手名）。
  **机械手扫片** `StartMap(机械手, 站点)` → `HandleResult<ModuleRoutine>`（`RobotMapRoutine`，LoadPort Load / 自动跑货 Unload 对账时调）：按机械手排队，
  轮到队头、机械手没在搬片、手上全空才调机械手的 `IWaferMap.Map`；排着时 `Admit` 里手上空了一律拒新搬运、手上有片只拒只取到手上的（`transfer.robot_mapping`），
  机械手停用 / 维护模式 / 出错或没初始化直接判失败；扫描时搬运先收尾再推扫片；中止搬运管理一起撤。决定见 decisions.md「扫片」。
  启动时问片归哪个 Job：开机 Bind 时取的 Job 管理的 `OwnerOf(片)`。EC：`StationWaitTimeoutMs`、`ManualWaitTimeoutMs`（手动服务等操作收尾）。
  服务 `ITransferService`（`xyz.Service\Transfers`）：`TransferAsync` 启动手动传片并等待操作收尾、`ReleaseAsync` 按晶圆标识释放保留资源（`transfer.not_held`）。
- **Job** `Job\JobManager`（machine.xml `Dispatch.Job`，扫描由 Dispatch 带着；子节点 `Task` = 任务元件 `TaskElement`（必须配），
  `Scheduler` = `SchedulerElement`（不配自己建一个））：SEMI E94 CJ / E40 PJ，决定见 decisions.md「Job」。
  **Job 是标准件**：CJ / PJ 管理、任务元件、调度引擎全在平台，机型不写 Job 代码，这几个类不开 `abstract` / `virtual`（JobSmoke 盯着）；
  调度逻辑不同的在平台另写调度元件（实现 `ISchedulerElement`），机型在 machine.xml 的 `Scheduler` 选 Type（现在只有 `SchedulerElement`，见 decisions.md「调度可以有多个」）；
  对外的任务、搬运请求、站内任务和执行结果契约保持通用；后续增加不同派单策略时，将结果收集和共用执行方法提到 `BaseSchedulerElement`。
  这是后续整理项，当前未新增调度基类或第二种调度实现。
  站点的差别靠站点自己声明、执行任务（`SupportedTasks` / `StartTask`），走哪些站点靠流程配方，规则要变就改平台。
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
    （放进 EAP 的上报派发元件 `IEapNotifierElement`（开机 Bind 时取），跟所有上报一条线程按先后发，PJ 结束先于 CJ 完成报）。
  - 任务元件 `Task\TaskElement`：建 PJ 时照流程配方快照（`ProcessJob.Sequence`，库里 `Find` 给的副本）生成任务表，一片一行（`TaskRow`）：来源 LoadPort 取片 →
    每一站放片、站内任务、取片 → 回片 LoadPort 放片（回片槽建 PJ 时定）。中间的路线整个 PJ 算一次（`BuildRoute`）：这一站的工艺配方取快照、站点组去掉用不了的
    （没装、停用、机械手到不了、跑不了这个配方）、剩下的每个站点都要声明支持用到的任务（`job.station_task_unsupported`）。每一格（`WaferTask`）记状态
    （`WaferTaskState` Waiting / Running / Done / Error / Cancelled）、实际站点和槽、机械手和手、出错原因（改状态给调度用：`Start` / `Done` / `Fail` / `Reset` / `Cancel`）；
    当前设备动作直接挂在 `WaferTask.Routine`（不序列化，结束时清空），不另建执行任务表；任务元件管状态，调度负责启动和收进度。
    每拍核对片位（不对记 `job.wafer_moved`）。出错停住等人：`Retry`（退回等着做）、`Complete`（人做完了；取放要片在账上正好在这一步做完该在的地方，
    不在回 `job.task_position_mismatch`）。片做没做成看晶圆账（`WaferInfo.ProcessState`），任务表不另记。
  - 调度引擎 `Scheduling\SchedulerElement`：从每行当前 `WaferTask` 执行；站内任务直接交给站点 `StartTask`（内部校验，拒绝就等待；`CheckTask` 只供单独询问），取片先选定并占住后续放片目标，再启动实际搬运操作。
    先起站内任务、再走机内的片、最后投新片；并发由目标槽、机械手和站点占用自然限制，不设机内片数上限。站点组按 machine.xml 先后挑第一个能放的，派不出去下一拍再看。
    每拍 `Collect` 直接读当前任务的 `Routine`，没有 `_moves` / `_works` 或 `Move` / `Work` 执行记录。取片确认后完成取片格，操作引用转到放片格，整行同时只有一格 Running。
    没碰到片就失败退回等待；动过片才失败将当前取片或放片格记 Error；站内操作被 PJ 中止打断的记未执行。
    不维护每拍槽位、机械手的重复占用集合，直接查询搬运管理；调度按先后尝试空闲且可达的机械手，手臂选择和资源校验只在搬运执行口做一次。
    收搬运进度按片（`Wafers[i].Progress`）：取完的片取片格记完成，放片格接着等或跟着交换走完。
    交换、预取没有开关，换不换看机械手的手（2026-10-20 起）：站里的片做完、手上有片等着进这一槽就派交换（排在"走机内的片"前面）；
    片的下一站满着、里面那片有去处时先把片取到手上（只取不放），机内的片和投新片都做（buffer 里等着进腔的片也提前拿上手），一个站只预取一片、
    一定留一只空手给旧片；单手机械手、许可不对称（某站只许某只手）挑不出第二只手，就不预取、不交换。
    machine.xml `PreferIdleStation`（默认关）：开着时挑站的三处（普通放片、交换、预取）把站点组按 `IProcessStation.LastStartedTimestamp` 排，最久没开工的在前
    （没开过工的、不是工艺站的当最早，一样的保持配方顺序），几个并行腔轮着用；关着照流程配方勾的顺序。
    工艺日志在任务状态方法里直接写，不再经任务开始 / 结束事件转发到 JobManager；EAP 片加工上报仍由 E90 照晶圆账报。
    只看每一行的许可（`TaskPermission`，PJ 状态定的：暂停、停止就体现在这上面）。
  - 站点任务：`TransferStation\StationTaskAction`（Pick / Place / Process，字符串常量，新站点要新的站内任务自己起名字）、`ITransferStation.SupportedTasks`（默认取放，腔体加工艺）。

  创建入口：`CreateProcessJobAsync(loadPort, pjName, slots, sequence, lotId)` 独立创建 PJ，LotId 跟着 PJ 进入快照和 `process_job` 记录；
  创建方法直接接收参数，不再使用 `ProcessJobSpec` / `ControlJobSpec`；服务通信仍用请求 DTO。CJ / PJ 创建参数、请求 DTO 和运行对象不存 AutoStart，
  启动方式由 Job 节点的 machine.xml `ProcessJobAutoStart`（默认 True）和 `ControlJobAutoStart`（默认 False）决定：True 准备好直接开始，False 等待对应的 Start 命令。
  E40 建 PJ 报文的 PRPROCESSSTART、E94 建 CJ 的 StartMethod 只校验格式、不覆盖 machine.xml；查询、上报和历史记录中的 AutoStart 反映设备配置。
  CJ / PJ 不保存 CarrierInstance，CJ 完成后检测到来源 LoadPort 的 Carrier.IsArrived=False 才删除；载具仍在位时保留结果。
  载具能不能分给 Job 由 `BaseLoadPortModule.CanUseCarrierForJob`（`IJobAccess` 上）判断（模块启用、载具到位、IsLoaded（正被机械手取放也算），接了 EAP 时槽图被 Host 认定；只管排活，机械手能不能进站看 `ITransferStation.CanPrepareTransfer`）；Job 创建、定片、回片目标选择、CJ 启动直接读取该属性，不在 Job 内解释 LoadPort 状态码。
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
  ④ 调度派任务（Manual 不派）→ ⑤ 发布当前快照（`JobListDto` 推送，留存，开机先推一份）。不使用 dirty 标记、快照版本号或任务表版本比较，每拍把全貌转一次 JSON：跟上次推的不一样才推（2026-10-28），跟上次交去写的不一样才交给存库线程合并写入（2026-10-22），上一批没写进库期间每拍都交。
  不限同时跑几个 CJ、不设 CJ / PJ 个数上限（一个 LoadPort 一个 CJ、一片只归一个 PJ，个数自然有数；S16F21 答 U2 最大值）。
  EAP 按载具号找：`IJobManager.FindControlJobByCarrier` 在 Job 锁内调用 `CjManager.FindByCarrier`，从 CJ 字典读取并返回 DTO 副本；`FindProcessJobsByCarrier` 在锁内读取当前 PJ，包括保留在 CJ 下的已结束 PJ。Snapshot 查询在锁内生成 DTO，不缓存 `_snapshot`。PJ 记着建的时候的载具号（`ProcessJob.CarrierId`，E40 报料 PrMtlNameList 也用它）。
  machine.xml：`IsEnable`、`ProcessJobAutoStart`、`ControlJobAutoStart`、`IsPersistent`、`Database`、`HistoryQueryMaxCount`；EC：`CommandTimeoutMs`。服务 `IJobService`（`xyz.Service\Jobs`：建 Job、CJ / PJ 命令、出错任务重做 / 标记完成）。
  **存库与重启**（2026-10-07 用户："界面上显示的就是数据库的数据，该存库就存库"）：CJ、PJ 各一张表，一个 Job 一行（`control_job` / `process_job`，
  实体 `ControlJobEntity` / `ProcessJobEntity` 继承 `BaseEntity`：自增行号、CreatedTime = 建的时刻、UpdatedTime = 最后写的时刻；PJ 行记着 CJ 的行号 `ControlJobRowId`，
  每片的任务明细是 `JobWaferDto` 列表的 JSON 放在 `Wafers` 列）。发布时全貌变了才把里面的 CJ、PJ 交给 Job 管理里的一条写库线程（同一个 Job 只写最新一份；
  第一次插一行、行号记在 `ControlJob.RowId` / `ProcessJob.RowId` 上，之后按行号更新），删掉的 CJ、结束的 PJ 在转换那里单独交最后一次。
  内存里不留历史、全貌里没有历史，界面看历史查库：`IJobService.QueryHistoryAsync`（`RpcParams.Start` / `End` / `Keyword`；按 CJ 建的时刻、关键字筛 CJ 名 / LotID / 载具号 / LoadPort，最新的在前，
  最多 machine.xml `HistoryQueryMaxCount` 个、多取一个判截断，回 `HistoryResult<ControlJobHistoryDto>`，带行号）、`GetHistoryProcessJobsAsync`（Parameters["ControlJob"] = CJ 行号，
  按 `ControlJobRowId` 取 PJ 转回 `ProcessJobDto`，任务明细 JSON 在映射元件里转回；行号不是数回 `job.not_found`）；服务从 `IJobManager.Database` 读库，表还没建回空的，读库出错回 `history.query_failed`。`Bind` 时 `CloseOutLastRun`：**重启后 Job 不接着跑**——
  库里还没删的 CJ 记成中止结束（`CompletedBy` 12、`EndedBy` 13、`Restarted` = true；完成了还没删的只补 #13），没结束的 PJ 记成中止（#16），
  任务明细留着重启前做到哪；机内的片由人确认片位收回后重新建 Job。两张表不清理（量小）。
  重启收场的 Job 不补报 E40 / E94 事件：开机时 Host 还没连上，报了也发不出去（不算断线，不进缓存）；Host 连上后用 S16F19、S14F1 查得到哪些还在。
  Host 的 S16 / S14 由 EAP 的 E40 / E94 翻成 `IJobManager` 的命令（见 §4「EAP」）。

## 4. 驱动：平台只给通道（`xyz.Drivers` + `xyz.Elements\Elements\Drivers`），品牌协议和指令在机型工程

2026-10-23 重做（用户定的，见 decisions.md「品牌驱动：平台只给通道」）：平台只提供**指令下发的通道和报文解析的通道**，不认识任何品牌、没有固定动作；
一条指令就是一个类（指令模式），报文解到"本品牌的报文对象"为止，比对、取值、判做没做完都在指令自己里面。加指令、换品牌只动机型工程，平台不动。

- 平台（`xyz.Drivers`）：
  - `Device\DeviceCommand`：`Key`（在途键，同一时间一键一条）、`BuildFrame()`（帧体，不带壳）、`Accept(object)`（是不是自己的回复）、`Complete()` / `Fail(原因)`、
    `IsCompleted` / `IsSuccess` / `Error`、`WaitReply`（工具用，扫描线程里别用）。`DeviceCommand<TMessage>`：只认本品牌解码器解好的报文，`Handle(TMessage)` 是抽象的，品牌指令都继承它、每条自己实现（品牌基类不实现，见下面机型一条）。
  - `Device\DeviceDriver`（sealed）：在途表（按 `Key`）、发送队列串行写、解析队列把报文挨个交给在途指令，谁认归谁，都不认的当 `Reported` 上报；`Abandon` / `AbandonAll` 作废。
  - `Communication\IFrameCodec`：`Encoding`、`Wrap`（包壳）、`Extract`（拆帧）、`Decode`（一帧解成本品牌的报文对象，认不出的返回 null 丢掉）；
    `IFrameLink`：有握手的协议给链路层（`Start` 发指令先发哪几帧、`Receive` 收到一帧回什么 / 吃掉 / 拒收作废 `Rejected`、`Reset`），品牌的解码器顺带实现（富创得读头的 ENQ/EOT/ACK/NAK）；
    `FrameCommunication`（收包泵 + 发送锁，拆好的帧原文和解好的报文（解不出来为 null）一起经 `MessageReceived(帧, 报文)` 往上交）；`ICommunication`（串口 / TCP，`CommunicationFactory.CreateSerial` / `CreateTcp`）。
- 平台（元件层）：设备通道（接口 `ICommDriver`；LoadPort、机械手、读头、对准器都用它）分**串口、网口两个元件**（2026-10-25，见 decisions.md「串口、网口各一个通道元件」）：
  `Elements\Drivers\SerialDriverElement`（machine.xml 节点 `Serial`：PortName / BaudRate / Parity / DataBits / StopBits）、`TcpDriverElement`（machine.xml 节点 `Tcp`：Host / NetPort），
  共用的在抽象基类 `CommDriverElement`：machine.xml `IsEnable`、EC 重连、报警、收发；子类只给 `CreateTransport()`（建串口 / 网口）。基类不能直接配在 machine.xml 里。
  一个设备下面串口、网口可以都配上，用 `IsEnable` 选一条：`CommDriverElement.FindIn(owner)` 只认直接挂的、启用的那条（启用两条开机抛，一条没启用返回 null、各设备照没挂通道处理），
  没启用的不建不连、不重连；同一台设备换接法（协议一样）只对调两边的 `IsEnable`，代码不动。
  用哪家的解码器由所属设备开机时 `UseCodec` 交给启用的那条（打开之前）；只认直接挂的，所以读头挂在 LoadPort 下面、自己再挂一条也不会认错。动作的 `Command` 步骤（`Elements\Routines\CommandStep`）发一条指令等设备回：
  每条从进这一步算超时，`UseTimeout` 换后面几条的超时；设备回错 `device_failed`、超时 `timeout`（先作废那条）、发不出去 `command_rejected`，被中止作废在途那条（见 §3「动作 = ModuleRoutine」）。
  `Elements\Drivers\RfidReaderElement<读码指令>`：读码流程和读码超时，读头类给 `CreateCodec`、`CreateReadCommand`、`CarrierIdOf`。
  `Elements\Drivers\StatusQueryLoop`（2026-10-26 从模块层挪下来，元件层的设备也能用）：状态轮询的节奏，一圈查什么由模块的 `QueryDataLoop()` 用 `yield return 指令` 给，回来了在 yield 后面取值写状态；
  `Scan` 回的是这一拍出了什么事（`StatusQueryEvent`：None / TimedOut / Disconnected），查到的值不从这里出来。**定时查状态一律写在 `QueryDataLoop` 里交给它**，别自己在扫描里定时 `Send`：
  它发之前给指令设 `DeviceCommand.IsPolling = true`（全仓只有这一处设），通道看这个标记把收发原文记到轮询文件。
  **收发原文在通道（`DeviceDriver`）里记**（帧体、不带壳，控制字符写成 `<CR>`、`<0x02>`）：放进发送队列记"发"，回复按认领它的指令记"收"；`IsPolling` 的进轮询文件 `poll-日期.log`（`LogHelper.InfoLoop`），
  别的进主日志（`LogHelper.Comm`）：动作指令、握手帧、没人认的（设备主动发来的）、解不出来的（写明"解不出来，丢掉"）。日志来源是建通道的串口 / 网口元件（`new DeviceDriver(帧通讯, 链路层, this)`）。
- 机型（`xyz.35021.Module\Drivers\<品牌>\`，35021 有 `Fcd`（LoadPort + `Rfid`）和 `Reje`）：`XxxProtocol`（常量）、`XxxMessage`（解好的报文）、`XxxCodec`（拆帧 + 解报文，有握手的顺带 `IFrameLink`）、
  `Commands\`（一条指令一个类、一个文件，继承本品牌的指令基类）。**每条指令必须自己实现 `Handle`**（用户 2026-10-23 定："必须实现，把品牌基类里那份删掉"、"都要这样设计"）：
  品牌基类只放共性（在途键、指令名 / 下发体、组帧），不写 `Handle`；认不认这条报文（名字 / 回显名 / 响应 ID）、ACK 算受理还是做完、INF / NAK / ABS / 错误码怎么判、数据怎么取，
  都写在每条指令自己的 `Handle` 里（富创得动作类 ACK 受理、INF 做完，查询类数据随 ACK 回；锐洁按回显名对号、错误码 00000000 是成功）。只有一条指令用的换算写在那条指令里（富创得的槽图字符 E / P / C / D 在取槽图指令 `FcdGetMapCommand` 里认，平台不认任何一家的字符），几条共用的才放品牌的 `XxxProtocol`（`FcdRfidProtocol.ErrorText`）。
  机型的模块类（`Loadport\LoadPortModule`、`Robot\RobotModule`）继承平台 `Base*Module`，只填 `CreateCodec()`、每个动作发哪几条指令、一圈查什么（`QueryDataLoop`）、主动上报怎么认（`OnReport`）；
  读头 `Drivers\Fcd\Rfid\FcdRfidReader : RfidReaderElement<FcdReadCarrierIdCommand>`。别的项目用同一品牌，把 `Drivers\<品牌>\` 和模块里填的那几行拷过去。
- 模块里通道的引用叫 `_driver`（公开属性、私有 set，跟 `_rfid`、`_carrier` 一样，用户定的），关连接写 `_driver?.Close()`。
- **在途指令要能作废**（2026-10-06）：通道按指令的 `Key` 占在途位（富创得按指令名、锐洁按整条指令（Pick 带手指、站点、槽号）、读头所有指令共用一个键），
  回复丢了就一直占着，同键指令再也发不出去。所以：等回复超时的发起方（`Command` 步骤、`StatusQueryLoop`、读头的读码）调 `Abandon` 让出来；`Close` 作废全部在途；
  模块动作没做成时 `AbandonAll`；读头拒收（NAK）链路层报 `Rejected`、通道 `AbandonAll`。作废的指令以失败落终态（Error 写 `Timeout` / `PortClosed` / `Abandoned` / `Aborted` / `NAK`
  这类英文标记）；迟到的回复没人认，当上报交给 `Reported`。
- **断线重连**（2026-10-06；10-15 加了次数上限，见本条末尾）：通道元件 `InitElement` 打开以后，扫描里发现断了就按 EC `ReconnectIntervalMs`（默认 5000）在后台先关后开
  （`Elements\Drivers\DriverReconnector`，网口 Connect 会卡几秒，不能在扫描线程上做），断开、恢复各记一次日志；Close 以后不再重连。
  连上以后模块的状态查询从头查（`StatusQueryLoop` 断线那一拍调模块的 `OnDisconnected`，锐洁在这里清订阅标志、下一圈重订手指在位推送）。
  `DeviceDriver.Open` 先收掉上一轮的收发队列再建新的；发送任务出错只关自己那一轮连接。
  `FrameCommunication` 一次打开一轮接收泵（`PumpSession`），旧泵出错只停自己那一轮——以前一个全局"在收"标志，旧泵在重连以后才醒会把新泵也停了。
  **重连有次数上限**（2026-10-15，用户："不需要一直重连"，所有驱动一起）：PLC 和通道元件都走 `DriverReconnector`，
  按 EC `ReconnectIntervalMs` 隔一次、最多 EC `ReconnectMaxCount` 次（默认 10，0 = 不自动重连），**连着满一个间隔才算恢复**、次数清零
  （串口打开就算连上：仿真器 / 设备不在时虚拟串口照样打得开，第一条指令写超时 1 s 又关掉，以前每 6 s 恢复、断开来回跳、次数一直清零停不下来）；用完还连不上就停下——
  通道报"设备通讯断开，已停止重连"（`ReconnectFailedAlarm`），PLC 不另报（掉线报警 `PlcOfflineAlarm` 一直在）；
  元件 `Reset()` 里 `Rearm()`，人工复位就马上从头再连。日志只记断开、放弃、恢复各一条；倍福连不上的原因同样的只记一次。
  PLC 以前在自己扫描线程上同步倒计时重连，现在也是后台重连，扫描不再被 ADS 连接卡住。
  **连接没成要关掉**：倍福 `ConnectAds` 以前 `ReadState` 抛异常（路由连上、PLC 不应答）时没 Dispose 新建的 `AdsClient`，
  每重连一次漏 2 条线程（ADS 接收线程 + 它的事件循环），Computer.Program 的线程数曲线一直涨才查出来。新写连接代码，每条失败路径都要释放。
- HSMS 协议（xyz.Secs 的 `xyz.Secs.Hsms` / `xyz.Secs.SecsII`）：设备端被动、独占绑定、单会话；S9 只由设备发；`PrimaryReceived` 在收包线程，别在里面同步等 SendAsync
  （xyz.Secs 的 `HsmsLink` 已经把 Host 的报文挪到自己的派发线程上，处理方不用操心这个）。
- 设备侧对象接 EAP 一律开三个口子：命令接口（EAP 和本地服务共用）、上报口（回调属性；报的时候放进 EAP 的上报派发元件 `IEapNotifierElement`（开机 Bind 时取一次），不自己起线程）、反查口（provider，为 null 走本地规则），
  照 `BaseLoadPortModule` 的 E87 / E84 写；接口放 `xyz.Elements\Interfaces`（EAP 元件在元件层，看得到）；细则见 decisions.md「EAP 接入的统一做法」。

### EAP（SECS/GEM，`xyz.Elements\Elements\Eap`，machine.xml 的 `Eap` 节点）
- **一个 SEMI 标准一个元件**，都是 `EapElement`（`Eap` 节点）的子节点：`Hsms`（E37 链路）、`Notifier`（`EapNotifierElement`，上报派发，必须配）、`E30`（GEM）、`E39`（对象服务 S14）、`E87`（载具）、
  `E90`（片跟踪）、`E40`（PJ）、`E94`（CJ）；E30 里的两块单独成元件：`Recipe`（S7 配方管理）、`Trace`（S2F23 / S6F1）。每个标准一个子目录（`Eap\E30` …），命名空间照旧 `xyz.Elements.Elements`。
- **协议在 xyz.Secs、元件只接设备**（2026-10-25 用户定：SECS 底层全放 xyz.Secs，上层只留直接调用和应用）：报文解析、状态机、回复、什么时候报什么事件都在 xyz.Secs 的引擎里
  （`xyz.Secs.Gem.*`：`E30Engine`、`E39Engine`、`E30RecipeEngine`、`E30TraceEngine`、`E90Engine`、`E87Engine`、`E40Engine`、`E94Engine`；几个标准共用的 `SecsRead`（读 Host 报文，结构不对抛 SecsException → 链路回 S9F7）、
  `GemValue`（值 ↔ SECS 格式、时间格式）、`E5Error`（ERRCODE + ERRTEXT，只用 E5 的码）、`SecsReply` 也在那边）。元件在 `Attach` 里建引擎，把自己（或一个小适配类）当口子交进去，口子的接口是 xyz.Secs 定的：
  - 设置：`IE30Settings` / `IE40Settings` / `IE94Settings` / `IE87Settings` 由元件自己实现，machine.xml 和 EC 照旧在元件上，引擎每次用现读；
  - 报事件：`IGemEvents.Report(代码, (名字, 值)…)`，元件翻成 `GemData` 走自己的 `RaiseEvent`（E30 和 EDA 都收到）；事件、DV 的声明（`[EventAttribut]` / `[DataVariable]`）还在元件上，
    代码用 xyz.Secs 的 `E30Codes` / `RecipeCodes` / `E87Codes` / `E90Codes` / `E40Codes` / `E94Codes`；
  - 设备和数据：E30 的编号表（`GemDictionary` : `IGemDictionary`，SV / EC / DV / 事件 / 报警）和存库（`GemStore` : `IGemStore`）、配方库（`E30RecipeElement` : `IGemRecipes`）、
    晶圆账（`E90Element` : `IE90Ledger`）、LoadPort（`E87PortDevice` : `IE87Port`）、Job 管理（`EapJobs` : `IGemJobs`）；日志 `SecsLog`（`ISecsSink` → LogHelper，记在所属元件名下：`new SecsLog(this)`；链路的报文明文开着 `LogMessages` 时记成收发原文进主日志）。
  `GemData` 留在框架（`xyz.Elements.Models`）：`RaiseEvent` 的参数要是 xyz.Secs 的类型，所有调它的工程都得引 DLL，所以口子收元组、元件里翻。
  **PJ / CJ 只有 Job 管理里一份**：E40 / E94 要的时候经 `IGemJobs` 现取快照（`E40ProcessJob` / `E94ControlJob`，只读、用完就扔），Host 的命令也经它翻成 Job 管理的命令，被拒的错误码经 `JobErrors`（框架）翻成 E5。
  直接用 xyz.Secs 类型的工程（`EapSmoke`、`HsmsSmoke`、`EdaSmoke`）自己引 `Libs\xyz.Secs.dll`，引元件层带不过去。
  不经元件层、单独用这个 DLL 的程序还得带 `CoreWCF.Primitives` 包：CoreWCF 在 xyz.Secs 里生成了模块初始化，DLL 一加载就要它（只用 HSMS 也一样，缺了开头就抛 TypeInitializationException）。
- **开机分两步**：模块开始扫描之前宿主调 `EapElement.Bind(LoadPort 们, JobManager, 流程配方库, 工艺配方库)`：链路没启用（`Hsms.IsEnable=False`）什么都不接；
  启用了按 E30 → E39 → Recipe → Trace → E90 → E87 → E40 / E94 接好（各自 `Attach`：建引擎、引擎登记处理方，挂设备侧上报口），**不开链路**；设备侧都起来以后宿主调 `EapElement.Open()` 才 `Hsms.Open`。
  Bind 要赶在扫描之前，是因为开机时端口上已有的载具第一拍就判到、自动读码，上报口没挂上的话到达和读码就丢了，E87 没有载具对象、Host 没法认定（2026-10-11 改的）；
  这时报的事 Host 收不收得到由 E30 管（没在线不报、没通讯按缓存设置），设备侧照常报。退出 `EapElement.Close`（先 Separate，再摘回调）。
- **链路 `HsmsElement`**：machine.xml 的连接设置和开关；开链路前查 DeviceId、被动模式下端口别跟 gRPC 撞。连接和分发是 xyz.Secs 的 `HsmsLink`（元件的 `Link`，引擎都在它上面登记）：
  `Handle(stream, function, 处理方)` 登记（重复登记开机就抛），Host 的 primary 放进一条派发线程
  按先后处理——处理方可以 await 设备侧的命令；返回 `SecsReply`（`Of(体)` / `Abort`（SxF0）/ `Error(n)`（S9Fn）/ `None`），
  `.Then(动作)` 是回复发出去以后接着做的（先让 Host 看到回复再报事件）。没人登记：整个 Stream 都没人管回 S9F3，否则 S9F5；
  先过 `Gate`（E30 挂的）。连上 / 断开（`LinkSelected` / `LinkClosed`）也排在这条线上通知。设备主动发用 `SendAsync`（没连上抛连接异常）。
- **E30**（xyz.Secs `E30Engine`；`E30Element` 管 machine.xml / EC、编号表、存库、报事件，操作员接口转给引擎）：通讯状态（链路连上后设备每隔 EC `EstablishCommunicationsTimeout` 发 S1F13，换过一次才算建立，之前 Host 的报文不理）；
  控制状态（EC：`InitialOnline` 默认 False、`OfflineSubState` 默认 HostOffline、`OnlineRemote` 默认 True、`OnlineFailedState`；
  离线时只收 S1F13、S1F17，别的回 SxF0；操作员接口 `RequestOnline` / `RequestOffline` / `RequestRemote`，界面还没做）；
  报告（S2F33 / 35 / 37，S6F15 / 17 / 19 / 21）、报警（S5F1 + 报警事件，S5F3 / 5 / 7）、SV / EC / DV / 事件名单、S2F15 改 EC（全查过再改、不报操作员改常量）、
  时间（EC `TimeFormat`；S2F31 默认只答收下，machine.xml `ApplyHostTime` 为 True 才改本机时钟）、缓存（S2F43 指定缓存哪些（S5F1、S6F1、S6F11）——默认什么都不缓存；
  断了通讯开缓存，开着时新报文也进缓存，Host S6F23 要了按先后发或清掉；状态和报文存库，重启接着开着）。S2F41 本机没有远程命令，回 HCACK=1。
  Host 定的报告、开关、缓存范围存 `gem_config`（一行 JSON），缓存报文存 `gem_spool`（machine.xml `Database` 指的库）。
- **E39**：各标准的引擎把对象类型登记进 `E39Engine`（`IE39ObjectType`：类型名、属性名先后 = 属性号、对象 ID、取属性；建、删、改可选）——
  Substrate、SubstLoc（E90）、ProcessJob（E40）、ControlJob（E94，S14F9 建 CJ 走它）。查要 ON-LINE，改 / 建 / 删要 REMOTE。
- **Recipe**（E30 的工艺程序管理；S7 报文在 xyz.Secs `E30RecipeEngine`，`Eap\Recipe\E30RecipeElement` 实现 `IGemRecipes`：两个库怎么找、怎么认、怎么存删）：分别挂到 `ISequenceLibrary`、`IRecipeLibrary`（`Interfaces` 下，两个库给 EAP 的那一面），上报口是 `IE30Callback`。
  **配方号就是配方名**（不加前缀，用户定的；两个库的名字不会重），按名字到两个库里找（流程配方库先）；Host 下一个两个库都没有的新名字，
  看 JSON 的样子由库自己认（`AcceptsSequence` / `AcceptsRecipe`：流程配方每步带 group、工艺配方每步带 values，`Recipe\JsonSteps.AllHave`）。
  S7F1 → PPGNT（0 可以、5 不在 REMOTE）；S7F3 下配方（PPBODY 收 A / B 的 JSON）→ ACKC7（0、1 不让：不在 REMOTE、新名字看不出是哪种、库没过检查，原因记日志）；
  S7F5 → S7F6 PPBODY 用 B（UTF-8），没有回空表；S7F17 删（空表 = 全删；有一个没有就都不删回 4）；S7F19 列（流程配方在前）。下、删要 REMOTE。
  配方变了报 `ProcessProgramChange`（DV `PPChangeName` = 配方名、`PPChangeStatus` 1 建 / 2 改 / 3 删；本地和 Host 改的都报）。
  machine.xml 只有 `LockLocalEditInRemote`（默认 False）：True 时 ON-LINE REMOTE 下 `IsLocalEditLocked` 为真，本地配方服务拒改。以后要前缀再加在这个元件的 machine.xml 里。
- **Trace**（xyz.Secs `E30TraceEngine` + 一个任务一个 `GemTrace`，`Eap\Trace\E30TraceElement` 只建引擎；给客户 FDC 供连续数据）：S2F23 `L[5]{TRID, DSPER, TOTSMP, REPGSZ, L{SVID}}`（SVID 也收整数数组），
  全部查完只回一次 TIAACK（0 收下、3 周期不对：DSPER 要 6 位 hhmmss 或 8 位 hhmmsscc、不能是 0；4 SVID 不认识或没给；5 REPGSZ 是 0）；
  TOTSMP=0 取消这个 TRID，同号再发替换。每个任务一条 `PeriodicTimer` 循环：一个周期以后开始，每次按 SVID 读当前值（跟 S1F3 一样），
  攒够 REPGSZ 次发一条 `S6F1 L[4]{TRID 原样, SMPLN, STIME, L{值}}`（SMPLN / STIME 是这一包最后一次，值按采样先后、每次按 SVID 先后），采够 TOTSMP 次不满一包的也发。
  S6F1 经 E30 的 `Send` 走发送线（离线不发、没通讯 / 缓存跟 S6F11 一样）；E30 离线（`WentOffline`）全部停掉；任务不存盘。
- **E87**（照老 CTC 写，定法见 decisions.md「EAP 各标准」E87 一条）：挂到每个 LoadPort 上（`IE87Callback` + `IE84Provider`，PortID 按传进来的先后从 1 编）。
  - 文件：框架这边 `E87Element`（声明 DV / 事件、实现 `IE87Settings`，设备回调和 E84 反查转给引擎）、`E87PortDevice`（`IE87Port`：一个端口在设备上的事实和命令——LoadPort、它上面的载具、片号表读写晶圆账）；
    xyz.Secs 那边 `E87Engine`（设备回调）、`.Host`（S3 报文）、`E87LoadPort`（一个端口：6 个状态机 + 载具号 + 放行标记 + `Device`）、`E87StateMachine`（小基类：转换表、发消息、进状态时带上原来的状态）和 6 个状态机类（搬运、存取方式、关联、载具 ID、槽图、取放，各自的状态枚举数值照 SEMI，没有载具 = 255）。
  - 写法：设备回调、Host 报文只做"翻成消息发给状态机"（`OnPort` 在锁里找端口、做事、最后刷搬运状态）；事件在状态机进状态时报，
    要设备动的（Load、Unload、重新读码、通知 E90）用 `Later` 攒着，出锁再做；写回设备的核对状态、载具号、片号表（`IE87Port.SetCarrierIdStatus` / `SetSlotMapStatus` / `SetCarrierId` / `WriteContentMap`）在锁里直接写（只拿设备的小锁）。
  - 设备侧的载具（到达、读码、槽图、取放）在 LoadPort 的 `ICarrier` 上，`E87PortDevice` 跟端口一起包成 `IE87Port`（引擎不认 `ICarrier`）；载具 ID、槽图、搬运状态机构造时接成 `_device`；
    Host 要重读码走 `IE87Port.ReadId()`。载具这一半的上报由载具经端口交给的入队口发，跟端口自己的上报同一条线。
  - 流程：读到号建对象等 Host（#1、#3），读码失败的 Host 带端口号给号（#1、#4）；ID 认定就 Load；读到槽图一律等 Host（#14）；
    第二次 ProceedWithCarrier 带槽图就比、对不上回 CAACK=3，片号表写晶圆账，槽图认定（#15）通知 E90 建片对象；Load 好就算在取放（#18）；
    干完了 E87 不自己卸（2026-10-09 起自动 Unload 归 LoadPort 的 EC `AutoUnload`，接不接 EAP 都生效），关着的等 Host CarrierRelease；取消、放行的卸好了端口转等取。
  - 搬运状态：`E87TransferStateMachine.Compute` 按 Host 启停用、设备 `LocalTransferState`、放行标记算，`Refresh` 转过去（等送、等取互换中间补挡着）；
    EC `PortPollMs` 定时、每次回调和 Host 动作后都刷；E84 每拍问 `E87Engine.TransferState(端口)`（就是 `Compute`）。
  - S3F17：ProceedWithCarrier、CancelCarrier、CancelCarrierAtPort、CarrierRelease、CarrierReCreate；S3F25：InService / OutOfService / ChangeServiceStatus / ChangeAccess；S3F27。
  - 不支持（回 CAACK=1）：Bind / CancelBind、CarrierNotification / CancelCarrierNotification、端口预约、读写标签（S3F29 / 31）、内部缓冲设备的动作；
    也没有 E39 的 Carrier / Port 对象、端口 SV、夹紧 / 松开事件。
- **E90**（xyz.Secs `E90Engine`；`E90Element` 挂在晶圆账上（`IE90Callback`）、实现 `IE90Ledger` 给引擎查账）：按账报片的位置（在来源 / 机内 / 回到载具）和工艺（要做 / 在做 / 做完 / 中止 / 没做成 / 跳过）、片位有没有片；
  接了 E87 时 LoadPort 上的片等槽图认定才建片对象。片位号：单槽的位置用模块名，多槽的用"模块名.两位槽号"。没有读片号的设备，片号核对不做。
- **E40 / E94**：Host 的 S16F11 / 15 / 5 / 17 / 19 / 21 / 27、S14F9 由 xyz.Secs `E40Engine` / `E94Engine` 解析、查过，经 `IGemJobs`（`EapJobs`）翻成 `IJobManager` 的命令（来源 Host），被拒的错误码经 `JobErrors` 翻成 E5；
  建 PJ 的料只收一个载具加槽号（槽表空原样交给 Job 管理 = 料到了取载具上正常的片），料可以还没到（Job 管理先建着等）；料没到时报料报要的槽号；
  不支持配方参数、暂停事件、改回片地方。
  PJ / CJ 的状态转换（元件收 `IE40Callback` / `IE94Callback`，交给引擎 `StateChanged`）报 PrJobSMTrans01~18 / CtrlJobSMTrans01~13。
- 冒烟：`HsmsSmoke`（链路和分发）、`EapSmoke`（各标准对假 Host、假 LoadPort、真晶圆账、假 Job 管理、假配方库）；
  两个真配方库的按名字列、取、存、删和上报在 `SequenceSmoke` / `ProcessRecipeSmoke` 里测；SECS-II 编解码、HSMS 本身在 xyz.Secs 仓库的 `tools\SecsSmoke`。

### EDA（Interface A，引擎在 xyz.Secs 的 `Eda`，框架这边 `xyz.Elements\Elements\Eda`，machine.xml 的 `Eda` 节点，2026-10-25）
给厂里 FDC / APC 这类系统采数据的 Web 服务（SEMI E120 / E125 / E128 / E132 / E134 / E138 / E164）。设计和我替用户定的在 decisions.md「EDA（Interface A）」。
- **结构：引擎在 xyz.Secs、框架只调它**（用户定：SECS 底层和 EDA 都放 xyz.Secs，上层只留直接调用和应用）。
  - xyz.Secs 的 `Eda`（命名空间照目录 `xyz.Secs.Eda.*`）：对外只有 `EdaServer` 和宿主要实现的四个接口——`IEdaSettings`（设备名、最大会话数、几个 EC，每次用都现读）、
    `IEdaEquipment`（自描述、读参数现在的值、SEMI 对象有哪些、对象属性）、`IEdaStore`（按块号整块读存）、`IEdaLog`；`Model\` 下的模型、`E164\` 的样例读法和标准事件翻译、
    `EdaEventContext` 也是公开的（宿主建自描述、交事件要用）。逻辑跟传输分开（用户定：Freeze 2、Freeze 3 都要，先做 2）：
    `E132\EdaSessionManager`（会话、问活、权限表、最大会话数）、`E125\EdaMetadataManager`（先照自描述查节点、参数在不在，再问设备侧）、`E134\EdaDataCollectionManager`（采集计划），
    都是内部类、只认模型；传输层在 `Freeze2`：`Freeze2Contracts.cs` 是用 dotnet-svcutil 从 `Freeze2\Wsdl`（SEMI 补充材料页免费下的 WSDL / XSD，原样不改）生成的服务定义和数据类
    （命名空间 `xyz.Secs.Eda.Freeze2`，生成代码不归硬规矩管，不手改；重新生成的命令见 decisions.md），五个服务类（接口发现、会话管理、安全管理、自描述、数据采集）
    把 SOAP 类型翻成模型、调三个 Manager，`Freeze2Consumer` 是设备往客户端发东西的口（`IEdaConsumer` 的实现），`Freeze2Host` 起 Web 服务。以后 Freeze 3 照这个在 `Eda\Freeze3` 加一层。
  - 框架这边：`EdaElement`（`IEda`、`IEventReceiver`，自己实现 `IEdaSettings`、`IEdaLog`，machine.xml 和 EC 都在它上面）、`EdaEquipment`（`IEdaEquipment`：
    `EdaMetadataBuilder` 照元件树建自描述，读值走元件，SEMI 对象问 E39；元件事件翻成 EDA 事件也在这里——哪个元件算哪种 GEM300 对象是框架的事，转换号对 E164 事件号是 `E164Events` 的事）、
    `EdaStore`（`IEdaStore`，存库）。`Eda` 节点下面没有子节点。
- **Web 服务**：EDA 自己单开一个 Kestrel（`Host` / `Port`，默认 0.0.0.0:8790，HTTP/1.1，跟 gRPC 那个口分开），CoreWCF 挂五个服务，SOAP 1.2、不带 WS-Addressing、XmlSerializer；
  设备往客户端发（采集报告、计划通知、问活、会话关了）用微软的 WCF 客户端包，同一套绑定。路径：`/E132InterfaceDiscovery`（客户端先连）、`/E132/SessionManager`、`/E132/SecurityAdmin`、
  `/E125/EquipmentMetadataManager`、`/E134/DataCollectionManager`。接口发现回的地址按客户端连进来用的主机和端口拼。
  `WebApplication` 调 `UseServiceModel` 要先转成 `IApplicationBuilder`（它也是 `IHost`，两个重载会撞）。
- **开机**：`Bind` 里（启用了才）建 `EdaEquipment`（照元件树和 GEM 编号表建自描述）和 `EdaServer`（读回权限表、最大会话数、持久计划，之前在跑的先休眠）。
  `OnStarted` 里订报警、`EdaServer.Start`（起问活循环、起 Web 服务；起不来只记错误，设备照跑）。宿主退出：退订报警，`EdaServer.Stop` 先停计划（攒着的发出去）、
  再关会话（给客户端发"会话关了"，最多等 3 秒）、最后停 Web 服务。
- **E132 会话**：客户端 ID 取建会话请求消息头的 `From`，回送地址取请求里的 HTTPEndPoint；一个会话一个 `EdaSession`，自己一条发送线程按先后发、一个客户端慢不拖别的；
  每个请求先 `Authorize(会话号, 权限)`。设备每隔 EC `SessionPingIntervalSec` 问一次客户端（每秒看一次，EC 改了马上按新的来），连着 EC `SessionPingFailLimit` 次问不到关会话；
  发一条最多等 EC `SendTimeoutMs`。最大会话数 machine.xml `DefaultMaxSessions`，客户端 SetMaxSessions 改过的存库、以库里为准（比现在的少不踢人）。
  **权限**：一个操作一项（`EdaPrivileges`，号是"标准号.操作名"）；权限表空着谁都能做，不空就看客户端直接给的、加上它归的角色给的；存库。
- **E125 自描述**（`EdaMetadataBuilder`，开机建一次、之后只读）：结构照 machine.xml 的树（分组也成节点，ElementType 是 Group；模块和下面挂着模块的分组算 Module，
  没子节点的 `IDevice` 算 IODevice，别的算 Subsystem；名字不合 E120 的换成合的、记告警；Uid 按路径算、重启不变）；参数是 SV / EC（看 Visible）/ DV（只在事件那一刻有），
  事件、报警照编号表挂到各自元件的节点上（报警号 = 报警全名"元件全路径.代码"，带 ALID、报出 / 清除 CEID）；参数怎么读也在这时记下（SV 走 `SvCollector.TryRead`，EC 读当前值按格式转回数）。
  EAP 链路启用（`IHsms.IsEnable`）时再按 E164 带上我们有的那部分：读编进 xyz.Secs 的 E164 样例（xyz.Secs 的 `Eda\E164\*.xml`，SEMI 原样，`E164Templates`）里的类型定义、状态机、SEMI 对象类型
  （PJ / CJ / 载具 / 片 / 片位，只留我们真报的转换事件、填上我们的 CEID），加 `JobManager` / `MaterialManager` 两个逻辑节点，整机节点上加 E30 控制状态机实例和几个标准参数（直接读我们 E30 的 SV / EC）。
- **E134 采集计划**：定义时 `EdaPlanCheck` 全查（节点、事件、报警、参数在不在；同一份里不重复；采样只能要随时能读的参数、周期不短于 EC `MinTraceIntervalMs`、几次一组合理、
  循环要有开始条件；开始停止条件和参数条件），毛病原样回。激活了建一个 `DcpRun`（发给激活它的会话）：事件经 `EdaElement` 的 `IEventReceiver.Report` 收（没有在跑的计划直接返回；
  `EdaEquipment.Resolve` 把元件事件翻成节点上的事件 + E164 标准事件，GEM300 转换按转换号对 E164 事件号，同号几个去向的按事件里的状态认，再 `EdaServer.Report` 交进去），
  报警由 `EdaElement` 订 `IAlarm.AlarmChanged`、`EdaServer.ReportException` 交进去；定周期采样一个一条 `PeriodicTimer`（`TraceRun`，开始时马上采一次），
  攒够几次一组发、采够次数或停止条件停（不满一组的也发）。攒批间隔大于 0 的计划攒着按间隔发。发送队列堆到 EC `MaxPendingReports` 丢数据、给各客户端发"跟不上了"，降到一半以下发"恢复了"。
  客户端会话没了：发给它的持久计划休眠（同一客户端 ID 再连上接着跑），不持久的停掉；它定义的不持久计划、它没别的会话了就删。持久计划存库，开机读回，之前在跑的先休眠。
- **存库**：`eda_config` 表（`xyz.Database\Eda\EdaConfigEntity`）一块一行 JSON：1 号行权限表和最大会话数，2 号行持久计划；machine.xml `Database` 指的库。
- **错误**：回在应答的 Error 元素里（E138 Error 的 code + 说明），号是本机定的（`EdaError`），说明用英文（协议文字，跟 E5Error 一样）；不进 ErrorCodes、不进语言包（没有界面）。
- 冒烟：`EdaSmoke`（假客户端：生成的客户端调设备、CoreWCF 起一个收数据的口；会话、权限、自描述、读参数、计划的定义 / 激活 / 停用 / 删除、采样、事件、报警、开始停止条件、
  E164 标准事件、持久计划休眠恢复和存库、问活、退出发会话关了）。

## 5. 服务（gRPC code-first）

### 加一个服务（清单）
1. 契约 `Shared\xyz.Shared\Services\IXxxService.cs`：
   ```csharp
   [ServiceContract]
   public interface IXxxService
   {
       /// <summary>
       /// 做某件事
       /// </summary>
       [OperationContract]
       Task<RpcResponse> DoSomethingAsync(RpcRequest request, CallContext context = default);
   }
   ```
2. **请求不建类**（2026-10-26 用户定）：一律 `RpcRequest`，参数按名字装在 `Parameters`（JSON 对象）里。契约注释一句短话说清是什么，不写参数
   （2026-10-26 用户改，原来写"参数：名字（类型）"）；要传什么看服务里的 `TryGet` / `Get`。
   参数名用 `Shared\xyz.Shared\Rpc\RpcParams.cs` 里的常量（值就是名字本身），客户端装、服务端取都用它，名字只写这一份；新参数名先加进去，已有的同义名字直接用（`Module`、`Index`、`Start` / `End`……）。
   返回数据放 `Shared\xyz.Shared\Dtos`（普通类，走 JSON），不加 `[ProtoContract]`——全仓只有 `RpcRequest`、`RpcResponse`（和事件流的信封 `EventMessage`）带它。
3. 实现 `Service\xyz.Service\<领域>\XxxService.cs`：`public class XxxService : BaseService, IXxxService`。取参数：必带的 `request.TryGet<int>(RpcParams.Index, out var index)`、`request.TryGet<string>(RpcParams.Module, out var module)`
   （一律写 `TryGet<类型>(名字, out var 变量)`：字符串、列表写成 `out string x` 会报可能为 null；几个写进一个 if，取不到回 `InvalidRequest()` → `rpc.request_invalid`，
   async 方法里 `return await InvalidRequest();`；名字不分大小写，没带、是 null、类型对不上都算取不到），可不带的 `request.Get(RpcParams.Keyword, string.Empty)`
   （类型对不上也悄悄用默认值，所以只给真正可选的参数用）。要用的单例在无参构造里
   `IocHelper.GetService<IXxx>()` 取一次、存成字段（宿主每次调用都新建一个服务，构造里取的就是当前的），方法里只判空、不再各自取
   （2026-10-18 用户："单例的不需要这个样子啊，为啥不通过构造"；取一律走 IocHelper，不写构造参数注入）；转 DTO 的服务在构造里取映射元件，没取到就是 machine.xml 配错了、直接抛。
   模块按名字找用 `FindModule<IXxx>(name)`（按接口从容器里取这一类的全部再按名字挑）。
4. 不用注册（2026-10-18 起）：开机按契约自动认（`ServiceExtensions.GrpcServices()`：装进来的 xyz 程序集里实现了带 `[ServiceContract]` 接口的公开类，
   DI 注册成 Transient，宿主照这张表 `MapGrpcService`；机型 DLL 里的也算），`ServiceExtensions`、宿主 `Program` 都不用改。
5. 错误码 + 两个语言包（§6）。
6. 冒烟里直接 new 服务类调方法验证（不起网络），见 `tools\WaferLedgerSmoke` 第 16 节。服务从容器里取模块和单例，
   冒烟里手搭的模块用 `SmokeIoc.Tree(...)` 换成容器里的那棵树；换了单例（`SmokeIoc.Use<IXxx>(...)`）要重新 new 服务（见 machine-and-tools.md §3）。

### 返回
- 成功：`RpcResponse.Ok()` / `RpcResponse.Ok(JsonHelper.Serialize(数据))`；客户端 `DeserializeData<T>()`。
- 失败：服务里一律写 `BaseService` 的 `Fail(ErrorCodes.Xxx, 参数...)`（2026-10-26 起所有服务都继承它，不在服务里各写私有的）。
  只有 `Task.Run` 的 lambda、async 方法、流式迭代器里要直接回 `RpcResponse` 的才写 `RpcResponse.Fail(ErrorCodes.Xxx, [参数...])`。
  **坑**：`RpcResponse.Fail` 只传一个字符串会绑到"只有消息"的重载，Code 是空的——没参数也要传 `[]`（基类的 `Fail` 没这个坑）。
- 元件的结果翻回包、元件没装，也用 `BaseService` 的：`Reply(result)` 只回成败（`HandleResult<T>` 也行，结果不带回去，比如开起来的动作），
  `ReplyData(result)` 成功把结果转 JSON 放进 Data（`HandleResult` / `HandleResult<T>` 都行），失败都照原样回错误码和参数；没装 `NotInstalled(ErrorCodes.XxxNotInstalled)`。
  async 方法里写 `return await NotInstalled(...)` / `return await ReplyData(result)`（跟 `InvalidRequest()` 一样）。要转 DTO 的自己写一个（`SequenceService.ReplySequence`），成功之外交给 `Reply`。
- 服务不往客户端抛异常：设备动作走 `BaseService.RunRoutine(port.Home(), port.HomeTimeout)`（被拒 → 入口给的错误码 + 参数、等超时 → `WaitTimeout`、失败 → `Failed(routine)`：动作的 Code/Args，不止一步的带停在第几步）；
  查库、读文件放 `Task.Run` 里 try/catch 转错误码。
- 找模块：`FindModule<IXxx>(name)`（按接口，`ILoadPort` / `IRobot` / `IChamber`），找不到 `ModuleNotFound(name)`。
- 一次回不完的大数据（数据曲线导出）走服务端流：契约 `IAsyncEnumerable<RpcResponse> ExportAsync(RpcRequest, CallContext)`，一段一条 `RpcResponse.Ok(JSON)`，
  每条远小于客户端收的单条上限（16 MB，见 decisions.md「gRPC 单条消息上限」）；出错推一条 `Fail` 就结束。迭代器的 try 里不能 yield，读下一段放一个返回"段 / 原因"的方法里接住
  （`DataChartService.Next`），读库放 `Task.Run`；客户端取消时 `context.CancellationToken` 停。

### 事件推送
- 两条总线（2026-10-27 用户定，互不相通、不转发）：推给界面用跨进程的 `RemoteEventPublisher`（后端发）/ `RemoteEventSubscriber`（客户端收）；后端元件之间要通知用进程内的 `EventBus`
  （同步、在发送方线程上调、传原对象、没有黑板），它发的不出进程。想知道别的元件现在的状态按接口问它，不靠黑板。
- 线：后端 `xyz.Service\Events\EventService`（客户端连上先发开流标记，再重放黑板，之后转发每条新发的）、客户端 `RemoteEventConnection`（client.md §4）；
  黑板两头各一块：后端给刚连上的客户端重放，客户端给后订阅的页面补发。客户端不往后端发事件，命令走 gRPC。信封 `EventMessage` 只在这条线上用，业务代码不碰。
- 发、收两半（2026-10-28 用户定）：后端 `xyz.Common\Events\RemoteEventPublisher`（`Send`、给 EventService 的 `SubscribeSent` / `GetRetained`），
  客户端 `xyz.Client.Common\Events\RemoteEventSubscriber`（`Register`、给 RemoteEventConnection 的 `Deliver`）；对面工程引用不到，客户端写不出 Send、后端写不出 Register。
  两边共用的只有 xyz.Tools 里的信封 `EventMessage` 和黑板规则 `RetainedBoard`（不带锁，用的地方在自己的锁里调）。
- `RemoteEventPublisher.Send(dto, token, retain)`：
  - 全局事件 DTO 带 `public const string EventToken`（"Account"、"AiStatus"、"Alarm"、"DataChartSignals"、"Ec"、"EquipmentStatus"、"Io"、"Job"、"Log"、"LogSettings"、"ProcessRecipe"、"RealChart"、
    "RealChartLayout"、"Sequence"、"SystemState"、"WaferLedger"）；
  - 模块状态 DTO 用模块名做 token、**留存**（客户端订上立即拿到当前值）；同一模块再推一种 DTO（腔体的设备状态推送 `ChamberDeviceDataDto`）也用模块名，类型不同互不覆盖；
    不是模块的部件跟着它上面的模块推（供液槽的 `TankDto` 装在机型 IPA 模块的 `IpaDto` 里一起推，token 是模块名，留存，变了才推）；
  - 界面收到就去拉数据的"变了"（配方库、流程配方库的变更 DTO、`AccountChangedDto`、`WaferLedgerChangedDto`）**留存**，元件开机再在 `PublishOnStartup` 推一条：
    界面晚打开、客户端比后端先起、后端重启、断线重连，都会先收到这一条、收到就拉，页面不用管连接（2026-10-28 用户定，原来不留存、页面挂 `ConnectionChanged` 连上重拉）；
    开机后就不变的（日志设置、AI 状态、两张曲线的信号表）开机直接推内容，跟服务回的是同一份（映射元件或元件自己的方法出，不各抄一份）。
  - 真正一次性的事（单条报警 `AlarmDto`、单个 EC 值 `EcItemDto`、一条日志 `LogDto`、实时曲线一帧）用 `retain: false`（不写黑板，同一格的旧状态擦掉）。
- **推送是元件自己的事**（2026-10-18 起，启动代码里不搭桥、没有各功能自己的 `*Publisher` 桥）：值一变的在变的地方发（EC 元件 `EcItemDto`、报警元件 `AlarmDto`、
  晶圆账 `WaferLedgerChangedDto`、两个配方库的变更 DTO、日志元件每条日志 `LogDto`，切晶圆尺寸时工艺配方库推一条编号 0 的）；每拍比、变了才推的在自己的 `OnScan` 里发
  （模块状态、整机的设备总状态、IPA）；按周期推的也在自己的 `OnScan` 里看时间（IO 整包按 EC `PublishIntervalMs`，实时曲线把数据曲线采到的行一帧一帧推）——
  所以要推的根元件在 machine.xml 配 `IsRun=true`。推失败吞住，同一个错只记一次日志。C# 事件（`AlarmChanged`、`ValueChanged`、`Wafer*`）留给后端里别的元件订（E30 订报警和 EC 变化，晶圆账自己接给 E90）。
- 转 DTO：映射元件 `IMapper` 的 `Map<T>(源)`；元件在开机 Bind 时取一次映射元件、服务在构造里取一次，都存成字段；不另写 `*Mapper` 类，服务里也不手写 `ToDto` 一个个字段抄。
  只有当场才知道的值（当前晶圆尺寸下的上限、腔体配了哪些 Type）由服务映射完再补；几样东西拼起来的（账上一个位置、带角色的用户）不算映射，照旧自己拼。
  映射元件 machine.xml 必配（开机就查，没配抛）。
  - **规则写在哪**：平台的在 `xyz.Elements\Elements\System\MapperElement` 的 `protected virtual void AddRules(TypeAdapterConfig config)`（要转的配方、流程配方数据类 10-27 起在元件层 `Models`，元件层都认得），
    构造里调一次就配好；同名字段、枚举转字符串不用配，名字对不上、形状不一样的才配一条。
  - **项目加规则**：继承 `MapperElement`、重写 `AddRules`（先调 base，只写规则、别用子类自己的字段），machine.xml 的 `Mapper` 节点 `Type` 换成项目的类。
    35021 是 `xyz._35021.Module.Mapper.MapperElement35021`（IpaModule → IpaDto）。
  - 按接口拿到的对象（模块的 `Tanks` 是 `ITank`）要照实际类的规则转：给接口配一条 `.Include<实际类, 目标>()`（槽就是这么配的）。
- 变化很密的（整篮 Mapping）只推"哪里变了"的轻通知，让界面自己攒一下再拉（`WaferLedgerChangedDto`）。

### 主界面用到的几处（2026-10-04）
- 系统设置 `SystemSettingsDto` 除了 Modules、Chambers，还带 `LoadPorts`、`Robots`（装配出来、启用的，先后同 machine.xml）：客户端主界面照它生成 LoadPort 页签和默认调度图。
- 机械手示教点 `TeachPointDto.Kind`（`StationKind`：LoadPort / Chamber / Other）：`BaseRobotModule` 跟槽数一样从搬运模块表认
  （`TransferManager.TryGetStation` 拿到的站点按接口认：`ILoadPort` / `IChamber` / 别的），表没绑好或不在表里算 Other；变了算状态变化。
- 设备总状态 `EquipmentStatusDto`（四色灯红黄绿 + `WaferSize` = 晶圆尺寸元件现在的尺寸，没配是 12）：整机模块 `CreateEquipmentStatus()` 算一次；
  整机每拍算，变了才点亮下面挂的四色灯（`ILight`）、推给顶栏（token "EquipmentStatus"，留存）。
  **系统状态、模式不在这里**（三色灯是三色灯，系统状态是整机自己的状态机），看下面「整机」。切晶圆尺寸 `IEquipmentService.SetWaferSizeAsync`（参数 `RpcParams.Size`）：
  整机和启用的模块都是未初始化（整机不算模块，单独查）、LoadPort 以外没片才切，切了写回 machine.xml（见 decisions.md「晶圆尺寸 8 / 12 寸」）。
- **整机** `System\SystemModule`（machine.xml 顶层 `System` 节点，必配、开机就查；`BaseStateElement`、不算模块（不实现 `IModule`，`ISystemModule` 自己声明 Name / State / Abort），在根上自己一条扫描线程；下面挂 `WaferSize` 和四色灯；
  machine.xml `Language`；单例按 `ISystemModule` 找）：
  状态 `SystemState : ModuleState`——NotInit / Initing / Idle（Manual）/ Aborting（停止中）/ Error 直接用基础的，加 AutoIdle 100、AutoRunning 110、Returning 120；
  状态表 `SystemStateTable`。动作入口（在模块锁里查状态再挂）：`InitAll()`（两步 `SystemInitStep`："机械手回原点"先 `IRobot` 的 `InitModule`，收完尾再"其余模块初始化"；
  被拒照原样回模块给的原因（错误码 + 参数，第一个参数就是模块名）/ 没做成 `system.init_failed` [模块]）、`Auto()` / `Manual()`（当场切、不挂动作；拨搬运管理的自动派单，Job 只看那个开关，开关只由整机拨）、
  `ReturnAll()`（锁里查状态 Idle 和搬运管理、晶圆账开着；锁外 `SystemReturnRoutine.Prepare` 查 Job、每片去处、倒着走的路线、用得到的设备、在位传感器，
  有一条不过当场回原因；进锁再核一遍状态才挂上 `SystemReturnRoutine`：一次一趟、手上的 → 其余站点 → 工艺站，经搬运管理（Recovery）搬，见 decisions.md「全部回片」）、`Abort()` = 整机停止（一步 `SystemStopStep`"等各模块停下"：先进停止中顶掉初始化 / 回片，锁外关自动派单、撤搬运、
  给在做动作的模块发中止（在搬运的机械手、给 Job 做工艺的腔体除外）、Job 全部走中止；等停的那一刻各模块手上的动作和撤掉的搬运都收完尾才回到停之前该在的地方）。
  每拍 Auto 下按 `JobManager.HasRunningJob`（CJ 执行中 / 暂停 / 中止中）切自动运行 / 自动空闲。动作没做成先不进 Error（报警逻辑还没接）：初始化回 NotInit、回片回 Idle。
  推送 `SystemStateDto`（token "SystemState"，留存，变了才推）；另推各模块状态表 `ModuleStatusListDto`（每个模块 Name / Kind / State / Mode，token "ModuleStatus"，留存，
  每拍比一次、有一个变了推整张，顶栏模块行用）。开机 `Bind()` 认容器里的模块（`IocHelper.GetServices<IModule>()`，不含自己），
  搬运管理、Job 管理、晶圆账、报警、晶圆尺寸也在这时取一次；回片、停止两个动作要用的由构造传进去。系统设置的模块名单不带它（没有 IO）。
- 整机操作 `IEquipmentService`（`xyz.Service\Systems\EquipmentService`）：`InitAsync` / `AutoAsync` / `ManualAsync` / `StopAsync` / `ReturnAllAsync` 都转给整机，
  开了就回、只翻结果；没配 System 节点回 `system.not_installed`，状态不允许回 `module.state_not_allowed` [System, 状态码]，Auto 没装 / 停用搬运管理回 `transfer.not_installed` / `transfer.disabled`。

### AI 助手（2026-10-20，见 decisions.md「AI 助手」）
- **元件** `Elements\Ai\AiElement`（machine.xml 顶层 `Ai` 节点，不配就是没有 AI；接口 `IAi`：`Model`、`ExplainAlarmAsync(来源, 报警码, 界面语言, 取消)` 回 `HandleResult<AiAnswer>`）。
  只读、不动设备、不扫描、不起线程：请求在调用方（RPC）的异步流程里走，排队（machine.xml `MaxConcurrent`，默认 1）、超时（EC `TimeoutMs`，含排队）都在元件里管，不抛异常、回错误码。
  模型跑在厂内网另一台机器上（现场不能联网），元件只是传话的：用微软 `Microsoft.Extensions.AI` 的 `IChatClient`，经 `Microsoft.Extensions.AI.OpenAI` 按兼容 OpenAI 接口格式
  连 machine.xml `Endpoint`（带 `/v1`）、`Model`、`ApiKey`（本地服务一般空着，空着带占位的 `no-key`）；不重试（服务没开当场报）、网络层不限时（超时归 EC 管）；`CreateChatClient()` 是 `protected virtual`，冒烟换成假的。
  地址不是 http / https、没配模型名、`MaxConcurrent` 小于 1，`OnSettingLoaded` 开机就抛。元件初始化只建客户端对象（不连网）；`Bind` 取报警、日志、数据曲线三个单例。
- **交给模型的材料**（报警解释）：报警本身（来源、文字、等级、类别、报出时间、说明、处理办法）；数据曲线里名字以来源路径开头的信号（`.` 换成 `__`，按 machine.xml 先后、最多 EC `SignalCount` 个），
  报警前后各 EC `SignalWindowSeconds` 秒——数字量列起始值和每次变化（一个信号最多 20 次），模拟量写最小、最大、报警那一刻的值，都带现在的值（`DataSignal.Read`）；
  日志元件里最近的告警、错误日志（最新的 EC `LogCount` 条）。规矩（system）：只按材料说、不编、说不准直说、三段（原因 + 依据 / 先查哪里 / 处理建议）、不建议跳过联锁；
  回答语言跟界面（服务传整机的 `Language`，en 开头用英文，别的用中文）。EC `MaxOutputTokens`（发出去是 `max_completion_tokens`，有的本地服务不认）、`Temperature`。
  回答 `AiAnswer`：正文（去前后空白）、模型、带了几条日志、哪些信号、用时。每次结果记一条 Info 日志（只进文件，界面自己报）。
- 错误码：`ai.not_installed`、`ai.alarm_not_found` [来源, 报警码]（报警已不在当前报警里，不问模型）、`ai.timeout` [ms]、`ai.failed` [原因]、`ai.empty_answer`。
- **服务** `IAiService`（`xyz.Service\Ai\AiService`）：`GetStatusAsync`（配了回 `AiStatusDto`（模型名），没配 `ai.not_installed`）、`ExplainAlarmAsync`（`RpcParams.Source` 报警来源、`RpcParams.Code` 报警码；回 `AiAnswerDto`）。
  客户端：实时报警页上面一条 AI 工具栏，连上后端问一次 `GetStatusAsync`，配了才显示；选中一条报警点"AI 分析"，等的时候按钮点不了，结果用公共确认弹窗显示（下面列参考了几条日志、几个信号、模型、用时，"仅供参考"）。

### 账号：登录、用户、角色（2026-10-21，见 decisions.md「登录和菜单权限」）
- **元件** `Elements\System\AccountElement`（machine.xml 顶层 `Account` 节点，接口 `IAccount`）：machine.xml `IsEnable`（要不要登录，系统设置的 `IsLoginEnabled` 就是它；没配节点 = 不登录）、
  `Database`（库名）、`IdleLogoutMinutes`（闲置自动退出、客户端弹登录框，默认 15，0 = 不自动退出）、`PasswordMinLength`（默认 4）——后两个
  **放 machine.xml 不放 EC**（用户 2026-10-22 定的：账号安全的规矩不让 EC 页改），配错 `OnSettingLoaded` 开机就报。不扫描、不起线程，调用都在 RPC 线程上、一把锁。
  元件初始化建表、第一次开机预置角色（管理员、访客，还有工程师、操作员各项的默认档位在 `Defaults`）和内置账号 admin / admin；建表、预置没成后面一律回 `account.failed`。
  会话 `SessionDto`：登录名、显示名、角色、各项档位（管理员不带，全部能操作）、闲置注销分钟数；`GetSession(空)` 就是访客的。
  建、改、删账号和角色，存权限都推 `AccountChangedDto`（token "Account"，留存，开机 `PublishOnStartup` 也推一条），登录着的客户端重新取会话、两个设置页重拉。
- **权限项编码**在 `xyz.Shared\Permissions\PermissionCodes`（= 菜单 Code，客户端菜单和预置角色共用）；后端只存、只发，**不按权限挡任何服务**（用户定的）。
- **服务**（`xyz.Service\UserManger\`）：`IAccountService`（`LoginAsync`、`GetSessionAsync`、`LogoutAsync`（只记日志）、`ChangePasswordAsync`）、
  `IUserService`（列、建、改显示名和角色、重置密码、停用 / 启用、删）、`IRoleService`（列、建、改名和说明、删、取 / 存一个角色的档位）；没配 `Account` 回 `account.not_installed`。
- 错误码 `account.*`（登录不对一律 `account.login_failed`，不说是登录名还是密码不对；内置的账号、角色动不了 `account.builtin_user` / `account.builtin_role`；
  有人在用的角色不能删 `account.role_in_use` [角色名, 账号数]；访客角色不给账号 `account.role_not_assignable`）。

### 启动顺序（`ServiceExtensions` + 宿主 `Program`，只做框架的事，2026-10-18 起不点名任何元件）
**注册**（`AddXyzServices`，容器建好之前）：`MachineXml.Load` → `ElementLoader.Load`（装整棵树）→ `ElementLoader.Register`（元件按接口进容器、树本身也进）→
GEM 编号表、gRPC 服务（按契约自动认）进容器。这一步不开机，元件构造、`OnSettingLoaded` 里不取别的元件（容器还没有）。
**开机**（宿主 `builder.Build()` 之后、开始监听之前调 `app.Services.StartElements()`）：先 `IocHelper.ServiceProvider = 容器` →
查必配的节点（`System` 整机、`Mapper` 映射元件、`Log` 日志，没配开机就抛）→ EC 合并 → GEM 编号表合并 →
**元件初始化** `ElementLoader.InitAll`（根元件按 InitOrder、再按 machine.xml 先后：PLC 100 先连、IO 200 读点表，再各模块连驱动、
登记槽位，轴登记收发块；子元件基类递归，不动硬件）→ **Bind** `BindAll`（整棵树按 machine.xml 先后、先父后子：晶圆账开机恢复、整机 / 搬运管理 / Job 认模块、
两个配方库认站点和腔体、EAP 接各标准（不开链路；要在扫描之前，开机已在端口上的载具才报得进 E87）、数据曲线定信号表、实时曲线接上数据曲线）→
**开始扫描** `StartScanning`（machine.xml 里配了 `IsRun=true` 的根各起一条扫描线程——整机、PLC、IO、Safety、Computer、DataChart、RealChart、各模块、Dispatch；
挂在 Dispatch 下面的 Job、搬运管理跟着 Dispatch 扫）→ **OnStarted** `StartedAll`（EAP 开 HSMS 链路，Host 连进来时处理方、设备都在）→
**开机推送** `PublishAll`（整棵树按 machine.xml 先后挨个 `PublishOnStartup`，推的都留在黑板上，客户端连上就重放）。
开完机宿主才从容器里的 `Rpc` 元件取地址端口（`app.Urls`，默认 localhost:5000，只走 HTTP/2）、映射 gRPC 服务、开始监听；
退出时 `ServiceExtensions.StopElements()`（`StopAll`：先停各根元件的扫描、等手上这一拍扫完，再倒着 `OnStopping`；之后宿主才释放容器）。
新元件要连接、登记的写在自己的 `InitElement`；要认别的元件、订阅、开机恢复的写在 `Bind`；要等都扫起来的写在 `OnStarted`；界面一连上就要拿到的写在 `PublishOnStartup`；退出收尾写在 `OnStopping`；
要自己扫描、自己推的根元件在 machine.xml 里配 `IsRun=true`——都不用改启动代码。根元件之间有先后的配 machine.xml 的 `InitOrder`（读点表的排在 IO 之后，用 PLC 的排在 PLC 之后）。

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

- `using var db = XyzDb.Create(库名);` 每次用每次建；库名就是 machine.xml `Database` 分组下的节点名（Default = xyz.db、Io = io.db），
  `DataBaseElement` 装配时注册。没注册的名字回落到默认库。
- 实体：新表 snake_case（`[SugarTable("wafer_adjustment")]`）；流水按天分表：
  `[SplitTable(SplitType.Day)]` + `[SugarTable("xxx_history_{year}{month}{day}")]` + `[SplitField] DateTime OccurredAt`，主键 `long Id = SnowFlakeSingle.Instance.NextId()`；
  写 `Insertable(batch).SplitTable()`，查 `Queryable<T>().SplitTable(起, 止)`，多取一条判断截断。
- 流水元件的套路（报警、晶圆账）：machine.xml `EnableHistory` / `HistoryDatabase` / `HistoryKeepDays`，EC `HistoryBatchSize` / `HistoryBacklogWarning`；
  `OnSettingLoaded` 建 Channel 和写库任务（这时不碰库）；每天第一次写之前整张删过期日表。写库失败只记日志，不影响设备。
- 量小、要马上查到的（人工调整记录）直接同步写一张不分表的表，用到时 `CodeFirst.InitTables` 建表。
- **晶圆账存盘**（machine.xml `IsPersistent`，EC `SnapshotIntervalMs` 默认 500）：表 `wafer_current` 存"现在"每片一行（内部标识、位置、来处、载具、状态、
  走过的位置 `Path`——`WaferInfo.Path` 出了 LoadPort 以后待过的模块逗号隔开，移账一次记一站、回到 LoadPort 清空，全部回片倒着走靠它；老表补列时老行是空的），
  账一变（都走 `RecordHistory`）标脏，存盘线程隔一会儿整张重写（事务里删了再插）；**开机恢复之前不写**（开机那一刻的空账不能冲掉上次的）。
  `Restore()`（晶圆账自己的 `Bind` 里调：各模块元件初始化登记完槽位之后、开始扫描之前）：腔体、机械手上的放回去（同一个内部标识，流水记 `Restored`），**LoadPort 上的不恢复，以开机 Mapping 为准**，
  重启前在加工的记成中止，放不回去的（位置没装、越界、槽上有片）丢掉记告警。宿主退出（`ApplicationStopping`）调 `StopSnapshot()` 最后存一次。
- Job 记录：`control_job` / `process_job` 一个 Job 一行，跟着进度更新、不清理；数据中心的 Job 历史页经 `IJobService` 查它（见 §3 Job）。
- 账号：`account_user`（登录名、显示名、密码哈希 `PBKDF2-SHA256$次数$盐$哈希`、角色、内置、最后登录）、`account_role`（名字、说明、种类 普通 / 管理员 / 访客）、
  `account_role_permission`（角色、权限项编码、档位），在 machine.xml `Account` 的 `Database` 库里；行数少，不清理。老服务建过的 `User`、`Role`、`Menu` 三张表，账号元件开机看见就删（`LegacyTables`）。
- 工艺站开工片数：`process_daily_count` 一站一天一行（站点、日期 yyyy-MM-dd、片数、更新时间），默认库；`ProcessCounter` 开机读回（读不成今天、累计这次从 0 数），
  开工一片放进队列、后台一个读者给那一天那行**加 1**（没有就插一行 1），不写总数——时钟往回调到前一天也只是给那天加 1，不会冲掉那天的数。行数少，不清理。
- 部件寿命：`count_lifetime` 一个次数寿命元件一行（全路径、已用次数、更新时间）、`time_lifetime` 一个时间寿命元件一行（全路径、起算时间、盯 DO 的通着秒数），默认库；`CountLifetimeElement` 开机 `Bind` 读回，DO 每变一次放进队列、
  后台一个读者给那行**加 1**（没有就插一行 1），清零也走队列；跟开工计数一样只加不写总数。`TimeLifetimeElement` 盯计时 DO 的通着攒满 60 s、或者断了，给那行**加整秒**（零头留着），
  清零改起算时间、秒数归 0；秒数列带默认值 0（老表补列时靠它填老行）。行数少，不清理。
- GEM（E30）掉电保持：`gem_config` 一行 JSON（Host 定的报告、链接、关掉的事件和报警、缓存范围、缓存状态），Host 改了就同步写；
  `gem_spool` 缓存的报文一条一行（雪花号主键 = 先后，体是 SECS-II 编码的字节），断了通讯才写。见 §4「EAP」。

## 8. 配置文件

- 源：**machine.xml 归机型**（2026-10-13 从平台 `xyz.Configs\Config` 挪过来，用户认可："这个确实是个问题"——平台里放 35021 的配置，第二个机型没处放）：
  35021 的在 `xyz.35021\Service\xyz.35021.Module\Config\machine.xml`（跟 `Config\IO` 点表一起；UTF-8 BOM、Tab 缩进、节点带中文注释）。
  IO 点表（`Config\IO\DI.csv` / DO / AI / AO）由 `IoPointTable.Load` 经 `ExcelHelper.ReadCsv`（xyz.Tools）读：按表头取列、不分大小写、顺序不限，带引号的格子里可以有逗号。
  机型工程编译后 `DeployMachineToHost` 拷到宿主 `bin\...\net10.0\Config\machine.xml`（**运行时读的是这份**，宿主旁 `Paths.json` 指 `./Config`），
  只在源文件比宿主那份新时才拷：现场在运行目录改过的不会被编译冲掉。只编宿主不编机型工程，宿主没有 machine.xml、起不来（跟机型 DLL 一样）。
  平台不再带 machine.xml；读 machine.xml 节点的冒烟（Job / ProcessRecipe / Sequence / WaferLedger）在 csproj 里把机型那份链接进输出目录的 `Config\`。新机型照这样在自己工程里放一份。
- 启动时在同目录生成：`ec.xml`、`EcDefinitions.xml`、`SvDefinitions.xml`、`AlarmDefinitions.xml`、`EventDefinitions.xml`、`DvDefinitions.xml`；
  IO 点表 `Config\IO\*.csv` 来自机型工程。
- 节点顺序：System → Rpc → EC → Database → Alarm → Log → LedgerManager → Account → Plc → Io → Safety → Monitor（分组）→ Computer → DataChart → RealChart →
  LoadPort → Robot → Transfer → Sequence → ProcessRecipe → Job → Eap（Hsms、E30、E39、E87、E90、E40、E94）→ Chamber → …（平台元件在前，模块在后）。加元件时把全部 Value 和注释写进 machine.xml。
- 外部工具 ToolBuilder（工程 ToolBuilder / ToolBuilder.Core / ToolBuilder.Smoke；源码不在本仓库，各人放在哪不一定）
  按 `[Element]` / `[Machine]` 编辑 machine.xml，备份到 `Config\backup\`（已 gitignore）；工艺配方字段表（`ProcessRecipe.Fields`，「配方字段」页，规则跟后端一样）
  有专门的表格页；机械手示教点按普通元件编辑（2026-10-24 删了它手写的站点页、站点参数表和站点校验，参数全从示教点元件的 [Machine] 来）。
  改了 ToolBuilder 要跑它的 build.ps1 发布到 app-v2。

## 9. 日志

2026-10-26 重整（用户定的，见 decisions.md「日志重整」）：

- **后端记日志一律走 `LogHelper`**（不写 Console、ILogger、不直接取 NLog）；第三方库（ASP.NET Core 宿主、OPC UA）自己的日志不转、不记（2026-10-27 删了转接头）：宿主只 `ClearProviders()`，OPC UA 给空出口 `DefaultTelemetry.Create(_ => { })`；
  它们抛出来的错由调用的地方 catch 了用 LogHelper 记（启动失败、崩溃在宿主 `Main`，按钮操作出错在操作日志，PLC 读写失败在元件里）。客户端是另一个程序，照旧 `ClientLog`。
- **两层名字**（`ILogSource`，元件基类实现，装配时算好）：第一层 `LogModule` = 树上最顶上那个元件的全路径（带外面的分组名，如 `LoadPort.LoadPort1`），第二层 `LogPart` = 往下剩的路径（`Serial`、`RFID.Serial`），模块自己写的为空。
  元件里写 `LogHelper.Info(this, …)`；不是元件的地方写固定名字 `LogHelper.Info(LogHelper.HostModule, …)`（开机、宿主、machine.xml 读错这类记 `Host`）；动作（`ModuleRoutine`）、状态机、Job 管理里的 PJ / CJ 这类跟着谁就把谁作为 `ILogSource` 传进去。
  一行：`【时间】 【级别】 【模块】 【部件】 内容`（没有部件那一格不写；改之前的旧文件照样认）。
- **文件两种**（`Log\` 下，一天一个、超 100 MB 拆）：主日志 `xyz-日期.log`（运行、操作、动作指令收发原文）、轮询文件 `poll-日期.log`（状态轮询的一问一答，量大，日志历史页不查，只在机台上开文件看）。
  收发原文的级别那一格写方向（`LogHelper.Sent` 发 / `Received` 收）：`LogHelper.Comm`（主日志）、`LogHelper.InfoLoop`（轮询文件）。
- **操作日志**（谁点了什么）：`xyz.GrpcHost\ActionFilter`（gRPC 服务端拦截器，在 `AddCodeFirstGrpc` 里挂，只做 gRPC 这一层）把方法名、来路、请求头的操作人交给 `xyz.Service\IActionFilter`
  （实现是 `xyz.Service\ActionFilter`，跟宿主的拦截器同名；`AddXyzServices` 注册成单例、构造注入给宿主的 `ActionFilter`；记不记、怎么记都在它里面，冒烟经接口测它）：每个调用进来记一条（`操作人@IP 服务.方法 参数JSON`）、回包记一条（成功 / 失败 错误码 [参数] / 出错，带用时），
  第一层 `Operation`、只进主日志不推顶栏（`LogHelper.Operation`）。不记：`Get` / `Query` 开头的、`Subscribe` / `Publish`；参数原样记（密码也不打码，2026-10-26 用户定）、中文原样（`JsonHelper.Options` 本身不转码，客户端装的参数就是中文，2026-10-27）。
  操作人是客户端每次调用带的请求头 `xyz-user-bin`（`RpcHeaders.Operator`，UTF-8），服务里要"谁"的（账单调整、两个配方库、建 Job）也从这儿取：`RpcOperator.Of(context)`，没带记 `Unknown`；请求 DTO 上不再有 `Operator`。
- Warn 以上的运行日志进 `LogQueue`，由日志元件（`LogElement`，machine.xml 的 Log 节点）在它的 `Bind` 里接上：留最近 `RecentLogCount` 条（客户端连上补拉），每条推给客户端顶栏；接上之前的日志都在队列里攒着（最多 2000 条），不会丢。操作日志、收发原文不进队列。
- **保留天数**：日志元件 machine.xml `KeepDays`（默认 30，0 = 不删），`Bind` 时 `LogHelper.SetKeepDays` 设到 NLog 各文件目标的 `MaxArchiveDays` 并把配置重新挂一遍（NLog 只在打开文件时按天数清，不重挂要等换天）；主日志、轮询文件、客户端日志（经 `LogSettingsDto.KeepDays`）共用。
- **NLog**（`xyz.Common\Log\NLog.config`）：`async="true"`（调用线程只放进队列，设备线程不碰磁盘；队列满了丢弃，不让设备线程等）、`autoReload="false"`（热加载会冲掉代码设的天数）、不写控制台；
  宿主正常退出 / 启动失败 `LogHelper.Shutdown()`、未处理异常里 `LogHelper.Flush` 等几秒，不然崩溃原因还在队列里进程就没了。
- **日志历史页的模块下拉**：日志元件 `Bind` 时从元件树（容器里的 `IReadOnlyList<ElementBase>`）取最顶上那层（停用的不算）按先后，再加 `Operation`、`Host`，经 `LogSettingsDto.Modules` 给客户端；查询参数 `RpcParams.Module` 按第一层完全相同筛。
- 写法：中文、说清楚是什么、为什么、接下来会怎样（如"人工调整记录写库失败（账已经改了）"）。

## 10. 线程和错误处理

- 共享状态：`private readonly object _gate = new(); lock (_gate)`；**事件在锁外发**；给调用方的是副本/快照。
- 单读者 `Channel<T>` 做后台写库、EAP 回调；`Interlocked` 计数；`volatile` 快照替换。
- 配置错、装配错：抛 `InvalidOperationException`（带节点名和值），开机就暴露。运行期：返回 bool / null / 结果枚举 / 操作的 Code+Args，
  服务层翻成错误码；循环和计时器里 catch 住记日志，不让设备线程崩。
- 解析用 `CultureInfo.InvariantCulture`；计时用 `Stopwatch` / `Environment.TickCount64`。
