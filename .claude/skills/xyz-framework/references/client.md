# 客户端（WPF）

路径都相对 `xyz.Core\Client`。.NET 10，WPF 工程 `net10.0-windows`、其余 `net10.0`；Nullable、ImplicitUsings 全开。
MaterialDesignThemes 5（暗色）、CommunityToolkit.Mvvm 8、protobuf-net.Grpc（code-first）、ScottPlot 5、System.Reactive。

## 1. 工程和依赖方向

```
xyz.Client（壳，WinExe）
  → 功能模块 xyz.Client.Main / Alarm / DataCenter / Io / Manual / Recipe / Setting
    → Common\xyz.Client.Presentation（控件、样式、语言包、公共显示模型）
      → Common\xyz.Client.Common（RPC、远程事件（RemoteEventConnection 收线、RemoteEventSubscriber 订阅）、日志、EC/报警缓存、会话）、Common\xyz.Client.DataModels（BaseViewModel）
        → xyz.Core\Shared\xyz.Shared（契约）、xyz.Tools（EventBus、IocHelper、JsonHelper）
Common\xyz.Client.Modules：机型客户端模块的接口（IClientModule、[ClientModule]、ClientModuleLoader、IClientMenuProvider、ClientMenu、
  ClientViewKeys = 页面里能按机型换掉的一块的注册键）
```

- 客户端**不引用任何后端工程**，只走 `xyz.Shared` 里的契约（服务接口、DTO、错误码）。
- 功能模块之间不互相引用；要共用的东西下沉到 Presentation / Common。
- 壳不引用机型工程；机型客户端模块（如 `xyz.35021\Client\Module35021.cs`）运行时按目录扫描加载。
- 每个功能模块：`Views\`、`ViewModels\`、`Models\`，根目录一个 `ServiceExtensions.cs`（`AddXyzXxxServices()`）。

## 2. 启动顺序（`xyz.Client\App.xaml.cs`）

1. 挂全局异常（Dispatcher / AppDomain / TaskScheduler），一律 `ClientLog.Error("Client", ...)`；显示加载窗口。
2. `GrpcClientFactory.Initialize()`（默认 `http://localhost:5000`；客户端收的单条消息上限 16 MB，管后端发来的回包和推送；exe 旁 `client.json` 的 `GrpcAddress`、`MaxMessageSizeMb` 可改。后端收的不配，用 gRPC 默认 4 MB）。
   通道外面包一层私有的 `UserHeader`（gRPC 客户端拦截器）：每次调用都在请求头 `xyz-user-bin` 带上 `ClientSession.UserName`（UTF-8），后端记操作日志、
   账单调整和配方的操作人都从这儿取——请求 DTO 上不再带 `Operator`，页面里也不用填（2026-10-26）。
3. 问后端系统设置 `ISystemService.GetSettingsAsync`（3 s 超时，每 1 s 重试，最多等 30 s）→ `L10n.Apply(语言)`；
   返回的模块名、腔体名用来生成 IO / 腔体菜单，LoadPort、机械手名单用来生成主界面的 LoadPort 页签和默认调度图。
   后端一直不在就用 zh-CN、这些按模块生成的东西整次为空（主界面显示提示）；换语言要重启客户端。
4. `RemoteEventConnection.Initialize()`、`ClientAlarms.Initialize()`、`ClientEc.Initialize()`、`ClientAccount.InitializeAsync(设置)`
   （`SystemSettingsDto.IsLoginEnabled` 定要不要登录，要登录的先取访客的会话；后端不在当要登录、什么都只看，连上再定）。
5. 建 DI：`AddXyzClientServices(settings)`（壳 VM、平台菜单、各功能模块注册）→ `ClientModuleLoader.Load(services)`
   （扫 exe 目录和 `Modules\**\*.dll` 里带 `[ClientModule]` 的 `IClientModule`，按 Key 排序调 `Register`；机型后注册，
   同一个 keyed 页面键会覆盖平台的）→ 机型语言包 `L10n.AddPack(module.PresentationAssembly)` → `IocHelper.ServiceProvider = ...`。
6. 统一调一遍所有 `BaseViewModel` 的 `Init()`（各自 try/catch 记日志）。`MainViewModel` 最先注册，它的 Init 会把所有页面建出来，
   所以**页面的构造先于自己 VM 的 Init**。
7. 主窗口先放到屏幕外 `Show`、等 `ContentRendered`（所有页面套模板、排版、Loaded 一次做完），再挪回来最大化、关加载窗口。
8. `IdleLogout.Start()`（闲置自动退出，到点再弹登录框）；要登录、还没登录的弹登录框（`TopBarViewModel.ShowLoginAtStartup`，取消就当访客）。

## 3. 菜单和页面

- 菜单写在代码里，不进数据库：平台菜单在 `xyz.Client\Menus\PlatformMenuProvider.cs`；机型真有独有页面时实现 `IClientMenuProvider`。
  `ClientMenu(parentCode, code, sort, title?)`：`parentCode == null` 是一级菜单；**Code 就是页面的 keyed 注册键**；
  显示名取语言包 `menu.{Code}`（没配就显示 Code），按模块生成的菜单直接用 title（模块名）。
- 一级菜单：Main 1、Manual 2、Recipe 3、Alarm 4、DataCenter 5、Setting 6、Io 7。
  Recipe 下：Recipe.Sequence 1（流程配方）、Recipe.Process 2（工艺配方）。
  DataCenter 下：DataCenter.LogRealtime 1、DataCenter.LogHistory 2、DataCenter.DataChart 3、DataCenter.RealChart 4、DataCenter.JobHistory 5（Job 历史），都是只看。
  Setting 下：Setting.Ec 1、Setting.WaferLedger 2、Setting.User 3、Setting.Role 4。
- 页面注册（功能模块的 `ServiceExtensions`）：
  ```csharp
  services.AddSingleton<XxxViewModel>();
  services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<XxxViewModel>());   // 统一 Init 用
  services.AddKeyedSingleton<UserControl, XxxView>("Setting.Xxx");                    // 键 = 菜单 Code
  ```
  按模块一页的（IO、腔体手动）用工厂：`AddKeyedSingleton<UserControl>(code, (sp, key) => new IoView(...))`。
- **权限**（2026-10-21，见 decisions.md「登录和菜单权限」）：菜单 Code 就是权限项编码，常量在 `xyz.Shared\Permissions\PermissionCodes`（客户端菜单、后端预置角色共用）。
  `ClientMenu` 另有 `PermissionCode`（几页合成一项时写，如每个腔体手动页都是 `Manual.Chamber`、每个模块的 IO 页都是 `Io`；不写就是 Code）
  和 `Access`（`MenuAccess`：Normal 三档都有、NoHide 主界面不能隐藏、ViewOnly 报警 / 数据中心这类只能看的页只分隐藏 / 只看、AdminOnly 固定只给管理员）。
  不在某一页上的按钮级开关是功能项 `IClientMenuProvider.Functions`（`ClientFunction(挂在哪个一级菜单, 编码, 先后)`：整机控制 `Main.System`、报警复位 `Alarm.Reset`）。
  档位怎么算在 `MenuPermissions.LevelOf(menu)`（没配过的按"只看"）；角色页的权限表照声明的菜单、功能项排，不进库。
  壳（`MainViewModel`）按会话只建看得见的菜单，给每个页面根设 `Permission.CanOperate`（往下传）；页面里会改东西的那一块标
  `helpers:Permission.Operate="True"`（只看时灰掉，本身绑了 IsEnabled 的控件标在外面包的那一层）；功能项的按钮标
  `helpers:Permission.Function="{x:Static permissions:PermissionCodes.MainSystem}"`。只是界面上挡，后端不查（用户定的）。
- `xyz.Client\Views\PageHost.cs`：全部页面启动时挂上，当前页 Visible、其余 Hidden。
  **页面常驻：Loaded / Unloaded 只触发一次**。要知道页面显示没显示用 `IsVisibleChanged`，转给 VM（示例 `WaferLedgerView.xaml.cs` →
  `viewModel.SetPageVisible(...)`）；页面不在前台时不拉数据、不跑每帧的东西（TrendChart、Robot 控件隐藏时停 `CompositionTarget.Rendering`）。
- 没注册页面的菜单显示占位页。

### 主窗口顶栏（`xyz.Client\MainWindow.xaml`，2026-10-23 照用户的样稿改，见 decisions.md「顶栏模块行」）
- 平时 96 高、三行：日志下拉框、报警下拉框（各 30 高）、模块行（`ModuleStatusBar`：machine.xml 装了的模块一个 `ModuleChip`，通到最右边，一行放不下折行、顶栏跟着变高）。
  右边那块只占上两行：日期、时间一组（一个时钟图标），下面隔开是当前用户（角色在悬停提示里）| 注销 / 复位 / 关蜂鸣器竖排（20 高、宽度随字）| 最右边四色灯，都直接用顶栏的底色。
- 字号只两档：模块名（和菜单、状态显示）14，按钮、日志、报警、时间、用户名 12，不要小字。页面区高度保持 920（主界面槽位表 25 槽一屏放下）——壳上要加常驻的东西别另起一行。

### 主界面（菜单 Main，`xyz.Client.Main`，布局照用户 2026-10-04 给的图，见 decisions.md）
- 三块：左 **系统操作**（平台固定：系统状态徽标、当前模式、报警条数、晶圆尺寸，Auto / Manual / Stop / Reset / 初始化 / 全部回片；左栏最底下是 **晶圆颜色** 说明，
  `WaferLegendDotStyle` 圆点 + `WaferLegendTextStyle`，颜色跟圆片控件同一套 `Wafer*`，圆片加了新颜色这里也要加一项）| 中 **整机调度**（可替换）|
  右 **LoadPort 页签**（`UnderlineTabListBoxStyle`，一个 LoadPort 一个：载具 / 槽图状态 / 槽数、LotID、Sequence 选择框、创建 / 启动 Job、槽位表）。
- **以后机台变了，要动的就是中间和右边，都不用改主界面**：
  - 右边页签照后端系统设置的 `SystemSettingsDto.LoadPorts`（machine.xml 装了的 LoadPort，先后同 machine.xml）生成，3 个、4 个都不改代码。
  - 中间默认是 `Views\DispatchView`：照 `SystemSettingsDto.Robots` 一台机械手一张公共调度图 `DispatchMap`，站点按机械手示教点（machine.xml `Size12` / `Size8`）
    的 `Direction` 摆在上下左右，卡片按站点类型（`TeachPointDto.Kind`：LoadPort 花篮卡、腔体 / 其他单片卡）选，machine.xml 里多一个站点图上就多一张。
  - 机型要别的摆法（几台机械手、缓存位、对中台……）：在机型 `IClientModule.Register` 里
    `services.AddKeyedSingleton<UserControl, 自己的View>(ClientViewKeys.MainDispatch)`，后注册的生效，整块换掉；
    自己的图照样可以拿 `DispatchMap`、`StationCard`、`Robot` 控件来拼。主界面由 `MainPageView` 构造时按这个键从 DI 取。
- 系统状态（`ModuleStateBadge` 整条色块）：订整机推送 `SystemStateDto`，没连上 / 还没收到 = 未连接（灰），否则照整机状态码
  （`ModuleStates.SystemText` / `SystemTone`：未初始化灰、初始化中 / 自动运行 / 回片中蓝、空闲 / 自动空闲绿、停止中黄、报警红）；
  模式按状态算（`ModuleStates.IsSystemAuto`：自动空闲、自动运行 = Auto）。跟四色灯（`EquipmentStatusDto`）没关系。
  按钮按整机状态表开关：初始化（未初始化 / 空闲 / 报警）、Auto 和全部回片（空闲）、Manual（Auto 下）、Stop（停止中以外）、Reset（连上就能点）；
  初始化 / Auto / Manual / Stop / 全部回片走 `IEquipmentService`，被拒原因用 `ErrorText.Of(response, ModuleStates.SystemText)` 把状态码翻成字；
  Reset 跟右上角一样走 `IAlarmService.ResetAllAsync`；结果写顶栏日志。
  报警下面一行晶圆尺寸（`EquipmentStatusDto.WaferSize`，"12 寸"）+ 小按钮"切换"（`MiniButtonStyle`，切到另一种，`IEquipmentService.SetWaferSizeAsync`；
  能不能切后端查，不行的原因写顶栏日志）。
- 槽位表（`DenseDataGridStyle`，行高 24，25 槽一屏放下，大号在上）：片以晶圆账为准（`LoadPortDto.LedgerSlots`，账上没登记才按 Mapping）；
  状态列 `JobWaferStateDataGridTextStyle`（物理状态不正常的先显示交叉片 / 叠片 / 状态不明，否则显示工艺状态）。
  Sequence 是界面上选的（点创建 Job 时交给后端）：上面的 Sequence 框一选给全篮能做的片套上，⊕ 弹公共选择弹窗给这一片单独选，⊖ 清空（这片不做）；
  后来才放上 / Mapping 出来的片也套上面选的那个；交叉片、叠片、状态不明的没有 ⊕ ⊖。
- **创建 / 启动 Job**（2026-10-05 接上）：订 Job 全貌推送（`JobListDto`，token "Job"，留存），每个页签认自己 LoadPort 上没删的 CJ（`LoadPortJobModel.ControlJob`），
  断开时清掉。创建 Job = `IJobService.CreateAsync`（LoadPort、LotID、选了 Sequence 的槽；操作人在请求头里；启动方式由设备 machine.xml 决定，后端跟 Host 一样先建 PJ 再建 CJ），有要做的片、这个 LoadPort 上没 CJ 才能点；
  启动 Job = `ControlJobCommandAsync`（CJStart），Auto 下、CJ 是 WAITINGFORSTART（`ControlJobDto.StateWaitingForStart`）才能点。结果写顶栏日志。
  Job 的列表、详情、暂停 / 停止 / 中止这些按钮还没有界面（要先给参考图或出样稿）。

### 数据中心 · 日志（`xyz.Client.DataCenter`，2026-10-26 改，见 decisions.md「日志重整」）
- 日志历史页工具栏：起止时间、**模块**（下拉：全部 + 日志的第一层，选项是连上后端时取的 `LogSettingsDto.Modules`，machine.xml 的元件树最顶上那层再加 Operation、Host）、
  级别、关键字（模块 / 部件 / 内容）、查询 / 今天 / 最近 7 天；不要"类别"、不要"带通讯原文"的勾（用户定）。只查主日志，轮询文件不查。
- 表格（历史、实时都是）：时间 | 级别 | 模块 | 部件 | 内容；收发原文的级别显示"发 / 收"。顶栏日志栏模块后面跟部件。
- 客户端日志 `ClientLog`：NLog 异步写，留几天跟后端（`LogSettingsDto.KeepDays`，`LogViewModel` 连上时设），退出 `ClientLog.Shutdown()`、崩溃 `ClientLog.Flush`。

### 数据中心 · Job 历史（`xyz.Client.DataCenter`，2026-10-22，见 decisions.md「Job」）
- 工具栏跟日志历史一样（起止时间、关键字 `InputTextBox`、查询 / 今天 / 最近 7 天，右边共几条 / 截断提示），不自动查。下面四块 `ZoneBorderStyle`：
  上 CJ（查到的，最新的在前）| PJ（右边 760 宽），下 晶圆（左边 520 宽）| 任务。查完默认选第一个 CJ，选 CJ 调 `IJobService.GetHistoryProcessJobsAsync` 取 PJ（换得快时晚回来的旧结果丢掉），
  默认选第一个 PJ、第一片；晶圆、任务都是 PJ 那一行里带的，不再问后端。
- 结果列 `JobResultDataGridTextStyle`（行上 `Result` = `Models\JobResult`），任务表的任务列 `JobTaskKindDataGridTextStyle`（Pick / Place / Process 翻成字，站点自己起名的原样）、
  状态列 `JobTaskStateDataGridTextStyle`；"进行中"用白字（强调色跟选中行的底色撞）。出错原因按任务带的错误码 + 参数查语言包。

### 加一个页面（清单）
1. 功能模块里加 `Models\`、`ViewModels\XxxViewModel.cs`、`Views\XxxView.xaml(.cs)`。
2. 模块 `ServiceExtensions` 注册 VM（+ BaseViewModel 别名）和 keyed View。
3. `PlatformMenuProvider` 加 `ClientMenu`（注意后面的 sort 顺延；编码加进 `PermissionCodes`，只能看的页给 `Access = MenuAccess.ViewOnly`）。
4. 两个语言包加 `menu.{Code}` 和页面文字；角色页上"管哪些操作"配 `permission.{Code}_tip`。
5. 页面里会改东西的按钮、输入那一块标 `helpers:Permission.Operate="True"`（不标的话只看的人也能点）；预置角色要不要给这一项改 `AccountElement.Defaults`。
6. 样式缺的加到 `Presentation\Styles`，不在页面里写资源。
7. 编译；用离屏预览（见 machine-and-tools.md）出中英文截图看一遍。

## 4. ViewModel

- 继承 `xyz.Client.DataModels.ViewModels.BaseViewModel`；不用 `[ObservableProperty]`、`[RelayCommand]` 这类源生成器。
- 文件布局：常量（`private const` / `static readonly`）→ `#region Column` → `#region Command` → `#region Service` → 构造 → `Init()` → 其他方法。
  - Column：**私有字段紧挨在它的属性上面**，属性用 `SetProperty`；派生属性通知、命令刷新（`NotifyCanExecuteChanged`）写在 setter 里。
  - Command：`IRelayCommand` / `IAsyncRelayCommand(<T>)` 属性，在构造里 `new`。
  - Service：只放字段（服务代理、计时器、订阅句柄、私有状态），不放方法。
- 构造里只建对象（集合、命令、服务代理 `GrpcClientFactory.Create<IXxxService>()`、计时器、CollectionView），**不拉数据**。
- 拉数据、订阅事件放 `Init()`（由壳统一调，View 里不调）。Init 要能重复调：先 `Dispose` 旧订阅、`-=` 再 `+=`。
- 命令方法 `Do` 开头（`DoMove`、`DoConfirm`）；不是命令的方法不加 Do。会走 RPC / IO 的命令用 `AsyncRelayCommand`，方法返回 `Task`。
- 不在 `Init` 里同步等 RPC（`.GetAwaiter().GetResult()`）——旧代码有（Manual），新代码不要学。

### 调后端
```csharp
var response = await _service.MoveAsync(RpcRequest.Of(
    (RpcParams.FromModule, from),
    (RpcParams.FromSlot, fromSlot),
    (RpcParams.ToModule, to),
    (RpcParams.ToSlot, toSlot),
    (RpcParams.Reason, reason)));
if (!response.Success)
{
    string reason = string.IsNullOrEmpty(response.Code) ? response.Message : L10n.Get(response.Code, response.Args);
    ClientLog.Error(LogModule, L10n.Get("setting.ledger.move_failed", reason));
    return;
}
var data = response.DeserializeData<XxxDto>();   // 失败会抛 InvalidOperationException
```
- 请求一律 `RpcRequest`（2026-10-26 起没有请求类）：参数写成 `RpcRequest.Of((RpcParams.名字, 值), ...)`，名字用 `RpcParams`（`xyz.Shared.Rpc`）的常量、
  跟后端取参数是同一份，不手写字符串；类型照契约注释里的"参数："，必带的一定给，可能是 null 的字符串补 `?? string.Empty`；不带参数的 `new RpcRequest()`。
  排版：一个参数写在一行里；几个参数一行放得下（150 字以内）就一行，放不下一个参数一行。
- 错误显示一律"错误码 → 语言包"；没码的老接口才用 `Message`。
- 结果提示走顶栏日志栏：`ClientLog.Info/Warn/Error(模块名, 句子)`。不弹 MessageBox。

### 事件
- `RemoteEventSubscriber.Register<TDto>(TDto.EventToken 或模块名, handler)`：后端推的消息由 `RemoteEventConnection` 转到 **UI 线程**再派发，handler 里直接改绑定属性；
  返回的 `IDisposable` 存在字段里，Init 重复调时先 Dispose。黑板上有的（后端发的状态类）订上就会补发最后一条。
- **页面不管连接**（2026-10-28 用户定）：要的数据都订推送——后端开机推、留存，订上就补发，客户端比后端先起、后端重启、断线重连都会重放；
  要去拉的列表（账号、角色、配方、流程配方、晶圆账）订它的"变了"，收到就拉；日志设置、AI 状态、两张曲线的信号表直接订内容。
  只有要**显示**连没连上的才订 `RemoteEventConnection.ConnectionChanged`（`-= On; += On;` 再 `On(RemoteEventConnection.IsConnected)`）：顶栏灯、模块状态栏"未连接"、
  主页断开时清状态、配方 / 流程配方 / 晶圆账页断开时按钮点不了；客户端的公共缓存 `ClientAlarms`、`ClientEc`、`ClientAccount` 连上时重拉，页面从它们拿。
- 页面之间的进程内通知用 `EventBus`（不出进程、没有黑板、在发送方线程上回调），跟后端推送是两条总线，互不相通（2026-10-27）。
- 高频推送、查询请求用 Rx：`Throttle` / `Sample` + `Select(Observable.FromAsync(...))` + `Switch()` + `ObserveOn(SynchronizationContext)`（DataChart、RealChart）。
- 后端推的是"变了"通知、界面自己重拉时，要攒一下再拉（DispatcherTimer 节流，见 WaferLedgerViewModel.RequestReload）。

## 5. View

- 构造里只做 `InitializeComponent(); DataContext = IocHelper.GetRequiredService<XxxViewModel>();`，不调 Init；
  需要感知可见性时再挂一个 `IsVisibleChanged`。按模块一页的 View 由工厂把 VM 传进构造（IoView）。
- 自带 VM 的复用控件（LogBar、AlarmBar、ModuleStatusBar、手动页的 ChamberManualControl 等）自己建 VM、自己 Init，这是例外，不是页面的做法。
- 不写 `<UserControl.DataContext>`、不写 `UserControl.Resources` / `Window.Resources`；样式都在 Presentation\Styles。
  只跟这一处有关的显隐、变色可以写行内 `<X.Style>` + DataTrigger，能复用的做成 Tag 驱动的公共样式（见 §6）。
- 引用资源一律 `{DynamicResource}`：样式、颜色（`Dark*`）、字号（`SizeNN`）、文字（语言包 key）。**不写死颜色、不写死 FontSize**。
- 页面骨架：
  ```xml
  <UserControl d:DesignHeight="920" d:DesignWidth="1980" TextElement.Foreground="{DynamicResource DarkPrimaryText}">
      <Grid Background="{DynamicResource DarkWindowBackground}">
          <Grid Margin="8">  <!-- 行：Auto（工具栏）/ *（内容） -->
  ```
  工具栏 `<Border Style="{DynamicResource ToolbarBorderStyle}">`，里面用 34 高的 `Toolbar*` 控件，输入之间 `Margin="0,0,10,0"`；
  内容用 `materialDesign:Card Style="{DynamicResource DefaultCardStyle}"`。整个窗口是 1980×1080 的 Viewbox，页面区 1980×920。
- 文字：固定的标签、按钮、提示写在 XAML（DynamicResource）；要把数据拼进句子的（"{0} 槽 · {1} 片"、日志、错误）用语言包模板 + `L10n.Get` 在 VM/模型里拼；
  C# 里不写死界面中文/英文。
- 状态、颜色这类判断不进 VM：VM 给状态（bool、枚举、色调），XAML 用样式触发器换字、换色。

## 6. 样式（`Common\xyz.Client.Presentation\Styles`）

- 文件：Border / Button / Card / ComboBox / DataGrid / DatePicker / ListBox / TextBlock / TextBox / TreeView `Styles.xaml`，
  加 `Color.xaml`（老调色板，含 `Wafer*` 晶圆色）、`DarkColors.xaml`（暗色主题 token）、`FontSize.xaml`（Size10~Size28）。
- 合并在 `xyz.Client\App.xaml`，**顺序有意义**：后面的样式用 `StaticResource` 取前面的颜色、字号。新加样式文件要同时加进 App.xaml
  和各模块 `Properties\DesignTimeResources.xaml`（设计时用，各模块的列表目前不全）。`Theme\MaterialDesignTheme.xaml` 没人用。
- 命名：`{档位}{控件}Style`——档位 Default / Toolbar / Compact / Dense / Mini / Large，再加 Danger / Success / Outlined 变体
  （`ToolbarDangerButtonStyle`、`CompactTextBoxStyle`）；专用的叫 `{用途}{元素}Style`（`TopBarUserButtonStyle`、`DialogCardBorderStyle`）。
- 高度档：默认输入 40 / 默认按钮 44 / 工具栏 34 / 紧凑按钮 32 / 紧凑输入 28 / Mini 22。
- 颜色 token（DarkColors）：底 `DarkWindowBackground`→`DarkSurfaceBackground`→`DarkSurfaceVariantBackground`→`DarkControlBackground`、
  边 `DarkBorderBrush`、字 `DarkPrimaryText` / `DarkDisabledText`、强调 `DarkAccent` / `DarkAccentPressed` / `DarkAccentSoft`、
  状态 `DarkRunningStatus` 绿 / `DarkWarningStatus` 黄 / `DarkAlarmStatus` 红 / `DarkCommunicationStatus` 蓝 / `DarkInactiveStatus` 灰、
  悬停 `DarkHoverBackground`、遮罩 `DarkOverlayBackground`。新颜色先加 token 再用。
- Tag 驱动的公共样式（数据绑到 `Tag`，样式按值换外观）：`ShowWhenTagTrueStyle`（FrameworkElement，Tag=True 才显示）、
  `NullContentHintTextStyle`（Tag 为 null 才显示，做占位提示）、`PageOverlayBorderStyle`、`StatusDotBorderStyle`、
  `TopBarStackAccentButtonStyle`（有报警变红）、`WaferBarStyle`（Tag = 晶圆色调 Idle/InProcess/Completed/Error/Crossed/Double/Unknown，其他=空槽虚线；
  `WaferLegendDotStyle` 是它的圆点版，多一个 Unledgered = 账上没有）、
  `AlarmCountTextStyle`（Tag=True 红字，报警条数）、`MapStatusTextStyle`（Tag = CarrierSlotMapStatus，写"映射完成"这类字并换色）。
  DataTrigger 的 `Value="Move"` 这种字符串能直接匹配枚举。
- 表单一行"标签 + 值"（主界面系统操作、LoadPort 栏）：`FormLabelTextStyle` / `FormValueTextStyle`。表格行里的小图标按钮（⊕ ⊖）：`RowIconButtonStyle`。
- 需要行数据带特定属性的样式：`LogLevelDataGridTextStyle`（Level）、`PickDataGridRowStyle` / `PickDataGridCellStyle`（IsPickable）、
  `FreshDataGridCellStyle`（IsFresh）、`JobWaferStateDataGridTextStyle`（WaferState，主界面槽位表的状态列）、
  `JobResultDataGridTextStyle`（Result）、`JobTaskStateDataGridTextStyle`（State）、`JobTaskKindDataGridTextStyle`（Kind）（Job 历史页）。
- DataGrid 三档：`DefaultDataGridStyle`（行 46）、`CompactDataGridStyle`（行 32，日志、报警、IO、EC）、
  `DenseDataGridStyle`（行 27、格子留白小、不能点表头排序、外面套 `ListPaneBorderStyle`）+ `DenseDataGridTextStyle`。
  列文字用 `ElementStyle`；**要让样式来定 Text 的列用模板列**（DataGridTextColumn 会在 TextBlock 上设本地 Text，盖掉样式）。
  Material 的格子、表头留白由 `materialDesign:DataGridAssist.CellPadding` / `ColumnHeaderPadding` 决定（上下左右都算，表头高度也跟着它），
  表头样式里的 Padding、Height 不管用。
- **表格样式里的 CellStyle 不生效**：Material 会先给列套上它自己的单元格样式，选中行成了灰的。要自己的单元格样式（选中行强调色、
  `PickDataGridCellStyle` 这类）就在 DataGrid 上直接写 `CellStyle="{DynamicResource ...}"`（账单调整页、流程配方页、选择弹窗都这么写）。
  这样写之后单元格模板是 WPF 默认的、不认 CellPadding，字离左边多远只看列的 ElementStyle 的 Margin（默认 8）；
  表头左右也要是 8 才对得齐——`ZoneDataGridStyle` 已经配好（`ColumnHeaderPadding` 8,10）。
- 分块的页面（一页几块、每块带标题条，如流程配方页）：`ZoneBorderStyle` 包一块，`ZoneHeaderBorderStyle` 是标题条
  （左边 `ZoneTitleBarBorderStyle` 竖条 + `ZoneTitleTextStyle`，右边按钮 `ZoneHeaderButtonStyle` / 主按钮 `ZoneHeaderPrimaryButtonStyle`，
  检查不过的红字 `ZoneErrorTextStyle`、合计这类统计字 `ZoneStatTextStyle`），块里的表格 `ZoneDataGridStyle`；字段 `MetaLabelTextStyle` / `MetaValueTextStyle`。
- 一行一条、各列是输入框和下拉框的表（工艺配方页的工艺步骤）：不用 DataGrid，用 `ListBox`（`PickListBoxStyle` + `StepListBoxItemStyle`）。
  **列不写死**：表头是 `StepHeaderBorderStyle` 里一个横排的 ItemsControl（绑字段表 `Fields`），每一行也是横排的 ItemsControl（绑这一行的 `Cells`），
  列宽按字段类型定（`ProcessRecipeFieldModel.Width`），表头和格子用同一个宽；一格按类型三选一显示（`ShowWhenTagTrueStyle` 包着）：
  输入框（整数、小数、文本，`DataType` / 上下限 / 单位绑字段，`ToolbarTextBoxStyle`）、下拉（`StepComboBoxStyle`，34 高，Tag=True 红框，
  提示字不浮到框上面；第一项"—"是不选）、勾选框（开关）。跟着别的字段走的下拉（药液跟着摆臂）由这一行的模型换选项，原来选的不在新选项里就清掉。
  检查跟后端同一套错误码；值本身有问题报具体原因，输入框里还有没提交的错字时报 `recipe.process.cell_invalid`（看框里的提示）。
  点进行里的输入框、下拉框也要选中这一行：View 里 `PreviewGotKeyboardFocus` + `ItemsControl.ContainerFromElement`（见 ProcessRecipeView.xaml.cs）。
- 页内确认框（`PageOverlayBorderStyle` + `DialogCardBorderStyle`）：标题 `PanelTitleTextStyle`，主角 `DialogSummaryBorderStyle` + `DialogSubjectTextStyle`，
  说明 `DialogMessageTextStyle`、后端拒了的原因 `DialogErrorTextStyle`（没字时不占地方），确认按钮 `DialogConfirmButtonStyle`（Tag=True 红，删除、放弃用）。
- 三档表格样式都挂了 `Helpers\DataGridColumnFill`：页面启动时先建好、后挂进窗口，有星号列的 DataGrid 会把列宽算成最小列宽 20 又补不回来
  （表头、内容的字全被裁掉，看着像空表；报警两页踩过），它在可视宽度出来后检查、没铺满就让 DataGrid 重算。新表格用这三档样式就自动有，
  自己写 DataGrid 样式要 BasedOn 它们。离屏预览查不出这类时序问题，要用真客户端启动、UI 自动化读表头宽度看。
- InputTextBox 有焦点、而且敲了字还没提交时，VM 改 Value 不会冲掉框里正在输的字（故意的）；焦点在框里但没敲过字的照样同步
  （2026-10-03 改的：以前只看焦点，确认框关了再开、焦点还留在框里时，重命名框里留着上回填的名字）。

## 7. 控件（`Common\xyz.Client.Presentation\Controls`）

- **InputTextBox**（所有文本、数字输入都用它，不用裸 TextBox）：绑 `Value`（提交后的合法值，默认双向），不绑 `Text`；
  `DataType` Text/Integer/Decimal；`Minimum`/`Maximum`/`Unit` 只对数字；`EcKey="元件全路径.参数名"` 从 EC 取格式、范围、单位
  （界面上写的优先）；边输边查格式，回车/失焦提交时查范围；错了红框提示、Value 保留上次合法值；
  发送类按钮绑 `HasError`（`Mode=OneWayToSource`）错着不发；表格行里加 `materialDesign:ValidationAssist.UsePopup="True"`。
  没写 Style 时自动套 `TextBoxStyles.xaml` 里按类型的隐式样式（= `DefaultTextBoxStyle`）。**自定义控件的默认样式都这么给，别在构造函数里
  `SetResourceReference(StyleProperty, ...)`**：那是本地值，列表行、表格行模板里写的 `Style` 优先级比它低，写了不生效
  （2026-10-04 改的：之前 IO 页 AO 设定框、EC 页值框写的 `CompactTextBoxStyle` 一直没生效，实际是 40 高、上下各 4 的默认样式）。
- **DateTimePicker** + `QueryDateRange.Of/Today/LastDays/LastHours`：查时间段，分钟精度，截止那一分钟算进去。
- **TrendChart**（ScottPlot 5 封装）：喂 `ObservableCollection<TrendSeries>`，只用 `SetData` / `Append` 改数据；单一左 Y 轴，默认按真实值画，
  `TrendSeries.Offset`（信息表里填）不是 0 就按 值 + 偏移 画（用曲线自带的 `OffsetY`，不复制数据），悬停框、信息表照旧真实值。
  基准线（`ShowXLines` / `ShowYLines`，ChartPanel 右上角 X、Y 两个开关）写在 `TrendChart.ReferenceLines.cs`：线是 ScottPlot 的
  `VerticalLine` / `HorizontalLine`（放在曲线下面），轴上的标签、ΔX / ΔY 和箭头是浮层（截图带得上）；两根轴的刻度都包了一层
  `TrendTickFilter`（时间轴只认 `IDateTimeTickGenerator`，所以按它包），被标签压住的刻度只去字不去网格。
  浮层位置 = ScottPlot 像素 ÷ `PlotView.DisplayScale`（本机 1.5 实测对得上），图画完一帧（`RenderManager.RenderFinished`）再对一次；
  量刚改过字的浮层先 `InvalidateMeasure` 再 `Measure`，不然拿回旧尺寸。开关、表里"X1 处 / X2 处"两列在 ChartPanel 代码里
  `Source = Chart` 绑定（放大窗口要搬整块，按名字找的绑定在那边找不到图）。
- **ModuleStateBadge**（`Text`、`Tone`、`IsCompact`）：模块状态一律用它（整条圆角色块：灰未初始化 / 蓝动作中 / 绿就绪 / 黄中止中 / 红报错），
  不写"状态：xxx"文字。码 → 字和色调用 `Presentation\Models\ModuleStates`（按 LoadPort / Chamber / Robot / 其他分开，同一个码不同模块意思不同）。
- **PickerBox**（从一个库里选一项：左边文本、右边"…"按钮弹公共选择弹窗；**选东西不用下拉框**）：绑 `Value`（默认双向）；
  `ItemsSource` 候选、`Columns`（`PickerColumn`：`HeaderKey` 语言包 key、`Path`、`Width`，不写是星号列）、`ValuePath`（默认 Name）、
  `PickerTitle`、`Placeholder`、`IsEditable`（默认 false 只能选；true 也能手输）、`HasError` 红框；`Width` / `Height` 用的地方随便设，
  档位样式 `DefaultPickerBoxStyle` 40 / `ToolbarPickerBoxStyle` 34 / `CompactPickerBoxStyle` 28。
  选工艺配方的两处（流程配方页的工艺配方列、腔体手动页的配方框）候选都是 `Presentation\Models\ProcessRecipeOptionModel`（编号、名称、说明、时长），
  连上后端时拉 `IProcessRecipeService.GetListAsync`、订 `ProcessRecipeChangedDto` 重拉；回 `process_recipe.not_installed`（没装库）时 `IsEditable` 放开手输。
- **公共选择弹窗** `DialogService.ShowPicker(标题, 列, 数据, 当前值, 值属性)`：一个普通模态窗口（不是 DialogHost，不动主窗口），
  单选，双击或"确定"返回选中项，取消返回 null；打开时选中跟当前值对得上的那项，没有就什么都不选（不默认第一项）。不配 PickerBox 也能直接调（流程配方页"添加"选站点分组）。
- **公共确认弹窗** `DialogService.ShowConfirm(标题, 说明, 确定按钮字, danger, showCancel)`：跟选择弹窗一个样子，说明多行、长了滚动；
  danger = 确定按钮红色（中止、删除这类），showCancel = false 只有一个按钮（只是告知）。以后 Job 页的停止 / 中止用它。
- **另存为** `DialogService.ShowSaveFile(标题, 默认文件名, 过滤)`：系统的另存为窗口，选好回完整路径、取消回 null；Excel 的过滤用语言包 `common.excel_filter`。
- **进度弹框 ProgressOverlay**（查询、导出这类要等的都用它，见 decisions.md「数据曲线导出 Excel、公共进度弹框」）：放在页面根 Grid 最后，盖住整页、蒙暗，
  中间深色卡片：圆环 + 下面一行字 + 取消。VM 上一件事一个 `ProgressState` 属性，页面一行：
  `<controls:ProgressOverlay State="{Binding Export}" Text="{DynamicResource datachart.exporting}" />`（不知道进度的加 `IsIndeterminate="True"`，圆环一直转、不写百分比）；
  `ShowDelay` 默认 500 ms，过这么久还没完才弹。VM 里 `bool done = await Export.RunAsync((progress, token) => ...)`：开关、取消（`CancelCommand`）、
  后台 `progress.Report(0～100)` 回界面线程都在 `ProgressState` 里；被取消回 false，别的异常照抛；后一次顶掉前一次时弹框不提前关。
- **Excel / CSV**（`xyz.Core\Shared\xyz.Tools\Excel\ExcelHelper`，前后端共用，MiniExcel 1.46）：`ExcelHelper.Save(路径, 列, 表)` 按表写 xlsx，各表同一组列（`ExcelColumn` 列名、显示格式、列宽），
  第一行列名并冻结；表的行（`ExcelSheet.Rows`）写的时候才枚举，可以边收边写。`SaveCsv(路径, 列名, 行)` 写 CSV（UTF-8 带 BOM），`ReadCsv(路径)` 读 CSV
  （每行按表头取值，表头不分大小写）。写都是先写 `.tmp` 写完再换上去。放后台线程调。以后读 xlsx 也加在这里。
- **账号弹窗**（一样的卡片样子）：`ShowLogin`（登录框，开机和闲置退出那次多一行"取消就当访客"；`notice` 是标题下的黄字提醒，`DialogNoticeTextStyle`）、`ShowPassword`（改自己的密码要旧密码 / 管理员重置不要）、
  `ShowUser`（新建 / 改账号）、`ShowRole`（新建角色 / 改名和说明）。都是传一个异步回调去调后端：回 null 关框，回一句话就红字写在框里、框不关。
  密码用 `PasswordBox` + `DefaultPasswordBoxStyle`（不进 VM、不绑定）。
- **ModuleChip**（模块小框：圆点 + 名字，`Text`、`Tone`、`IsProduction`、`HasMode`）：圆点按色调——就绪绿、报警红、其他灰；`IsProduction`（生产模式）边框亮（跟就绪同一个绿 `DarkRunningStatus`），
  否则灭（灰 `DarkDisabledText`，跟灰圆点一样；别用 `DarkBorderBrush`，它跟顶栏底色 #454545 几乎一样、看不出框），边框 2 粗、框 28 高；`HasMode` 为 False（供液这类不分生产 / 维护）不画边框、粗细照留。
  控件不认状态码，谁用谁给；顶栏模块行用它，别处要显示模块状态和生产 / 维护也用它。
- **ModuleStatusBar**（顶栏模块行，自带 VM `ModuleStatusBarViewModel`）：订整机推的 `ModuleStatusListDto`（token "ModuleStatus"，留存），一个模块一个 ModuleChip（WrapPanel）；
  状态码按 `ModuleStates.TextOf / ToneOf`（按 `StationKind` 分）翻，悬停看状态字；断开时全部未连接（灰点、边框灭）。
- LogBar / AlarmBar / ModuleStatusBar 只在主窗口顶栏用。
- 实时报警页上面的 AI 工具栏（`ShowWhenTagTrueStyle` 包着、Tag = `IsAiInstalled`）：连上后端问一次 `IAiService.GetStatusAsync`，配了 AI 才显示；
  表格 `SelectedItem` 绑 `SelectedAlarm`（报警一变整表重建，按来源 + 代码接着选中原来那条），"AI 分析"结果用 `DialogService.ShowConfirm` 只一个按钮显示（见 backend.md §5「AI 助手」）。
- **DispatchMap**（调度图，Robot 手动页左边和主界面中间共用）：给 `RobotName`（机械手模块名）就自己建 `DispatchMapViewModel`、订机械手和各站点的推送
  （都是留存消息，不用另外拉）；机械手在中间，站点按它示教点的方向摆四周，每个站点一张 `StationCard`
  （`Kind` = LoadPort 用 `LoadPortInfoCard` 花篮卡，其他用 `ChamberInfoCard` 单片卡，用模板切、只建用得上的那张）。
  `CardWidth` / `CardHeight`（默认 280×233）、`RobotHeight`（默认 360）可设，卡片圆片直径跟着 RobotHeight 算（= 叉上的片）；
  一排放不下时那一排整体缩小（只缩不放），不裁站点。LoadPort 卡底栏六盏灯跟 `LoadPortDto` 走（通讯 / 在位 / 到位 / 报警 / 自动 / 手动），
  腔体卡的"配方"取 `ChamberDto.Recipe`。`ChamberInfoCard` 的五行字段哪一行没有值就整行不显示（2026-10-04 起，后端还没给步骤、时间这些）。
- **HoldButton**（按住类按钮，点动用）：按下发一次 `PressCommand`，松开 / 拖出按钮 / 禁用 / 藏起来 / 卸载发 `ReleaseCommand`，中间不发别的
  （2026-10-26 去掉了每 200 ms 的续命）；两个命令共用 `CommandParameter`；不用 `Command`。样式照普通按钮套。
- **三维硬件**（`Controls\ThreeD`，说明见那儿的 README）：Arm / Lift / FluidPipe / Bowl / HomeCup / Door / Disk / ChamberBase 组件，
  `ChamberScene` 把它们装成腔体手动页的三维图（`Devices` 绑 `ChamberDeviceDataModel`、`Wafer` 绑晶圆账的片），视角工具栏在图下面。
  LoadPort（`LoadPortVisual3D`）和花篮（`CarrierVisual3D`，片用 Disk）两个公共组件由 `LoadPortScene` 组合成 LoadPort 手动页中间的三维图
  （槽数跟 machine.xml 的 SlotCount、每个动作 0.5 s，见 decisions.md「LoadPort 手动页（三维）」）；原来的二维花篮控件 `LoadPort` 删了。
  供液槽：`TankVisual3D`（透明圆筒 + 液体）由 `TankScene` 装成三维图，`Tank` 绑槽的显示模型（`Models\TankModel`，后端跟着供液模块推：35021 的 `IpaDto` 里带各槽的 `TankDto`，token 是模块名）、
  `WallTransparency` 罐壁透明度；液面跟液位，罐右边的信号灯跟各自的 DI 信号、machine.xml 里没配的不画。还没接进页面（Tank 手动页没做）。
- **SignalLamp**（信号灯：一盏灯 + 名字，对一个 DI 信号）：`IsActive`（触发亮绿、没触发灭、null 读不到是"不知道"色）、`Text`（machine.xml 节点名原样）、
  `IsAlarm`（报警位，触发亮红）。灯的颜色、字用 `SignalLampBorderStyle` / `SignalLampTextStyle`。显示 DI 信号都用它（界面件对后台元件的定法见 decisions.md）。
  三维对象不在逻辑树里，颜色用 `DarkHardware*` token（XAML 里 StaticResource、代码里 TryFindResource），不注册逐帧事件。
- **圆片没有右键菜单**：右键建片 / 删片 2026-10-10 删了（二维 `Wafer`、三维 Disk、机械手、各站点卡片、Transfer 页），建片统一在账单调整页做。
- **腔体手动页**（`xyz.Client.Manual\Views\ChamberManualControl`，布局照用户给的参考图，见 decisions.md）：数据都来自后端的设备状态推送
  `ChamberDeviceDataDto`（结构跟 machine.xml 一样：门、Bowl、卡盘、各条摆臂带它的 Lift 和喷嘴）——`ChamberDeviceDataModel.Update` 照树取三维要画的；
  VM 从同一份推送取轴页签（卡盘 + 各条摆臂，`AxisTabModel`）和气缸表（门、Bowl、各臂的 Lift，`CylinderRowModel`）。界面不再抄后端的属性名、方法名（2026-10-10 删了 PartValueNames / PartActionNames / PartKinds，Part 的叫法也去掉了）。
  动作调 `IChamberService` 的具体方法（`AxisHomeAsync` / `AxisMoveAsync` / `AxisStepAsync` / `AxisJogAsync` / `AxisStopAsync` / `AxisResetAsync`、`CylinderUpAsync` / `CylinderDownAsync`）；
  点动用 HoldButton：按下发一次 Jog、松手先等点动请求回来再发 Stop。轴参数（移动速度、点动速度、步距）默认值取这根轴的 EC，`ClientEc` 拉到后补上空着的。

## 8. 语言包（`Common\xyz.Client.Presentation\Localization`）

- `Strings.zh-CN.xaml`、`Strings.en-US.xaml`，`<sys:String x:Key="...">`，**两份的 key 必须一一对应**（加一条就两边都加）。
- **界面上显示的都要多语言**（标题、按钮、状态、提示、报错，用户 2026-10-04 定的）；从数据库、配置查出来的数据
  （machine.xml 里的模块 / 部件名、配方名、日志内容、片号）原样显示，不翻。后端报错参数里带的固定标识也要换成叫法再显示，
  比如设备动作名（Shared 枚举 `ChamberDeviceAction`，如 ValveOn）走 `DeviceActionText.Of` → `device.action.{转小写下划线}`，中文界面不露英文。
- key 小写点分、叶子 snake_case：错误码（= `xyz.Shared\Errors\ErrorCodes.cs` 的常量值，如 `wafer.slot_occupied`）、`module.state.*`、
  `common.*`、`shell.*`、`setting.ledger.*`、`menu.{Code}`（Code 保留大小写）；枚举值做叶子时保留原样（`setting.ledger.process.InProcess`）；提示文字 `_tip` 结尾。
- XAML 用 `{DynamicResource key}`；C# 用 `L10n.Get(key, 参数...)`（`string.Format`，可写 `{0:00}`）、`L10n.Get(code, response.Args)`；没有这个 key 时返回 key 本身。
- 机型独有的文字放机型的 `*.Client.Presentation\Localization\Strings.{lang}.xaml`；框架页面（包括机型挂的框架菜单）的文字放平台包。
- 界面语言由后端 machine.xml `System` 节点的 `Language` 决定，客户端启动时问一次。

## 9. 显示模型

- 公共的在 `Presentation\Models`（`ModuleStates`、`ModuleStateTone`、`QueryDateRange`、`TrendSeries`、`InputDataType`、`ProcessRecipeOptionModel`、
  `RobotModel` / `RobotStationModel`（机械手和它的站点，Robot 手动页、调度图共用）、`StationWafers`（LoadPort 花篮 / 腔体片位 → 圆片，以晶圆账为准）、
  `DiSensorModel`（一个 DI 信号：配了没有、名字、`IsActive`）、`TankModel`（槽：液位 + 五个信号）……），
  页面自己的在功能模块 `Models\`。
- DTO → 模型：简单的 `dto.Adapt<T>()`（Mapster，整体替换）；要保住实例的（动画、选中状态）写 `Update(dto)` 就地改；也可以构造里手写映射。
- 当前用户 `xyz.Client.Common.Session.ClientSession`（2026-10-21 起有登录）：`UserName` 记操作人用（不用登录时是 Admin，没登录是 Guest；`GrpcClientFactory` 的 `UserHeader` 每次调用都带上它），
  `LoginName` / `DisplayName` / `RoleKind` / `IsLoggedIn` / `IsAdmin`、`LevelOf(编码)` / `CanOperate(编码)`；会话一变发 `Changed`（界面线程）。
  登录、注销、刷新会话走壳里的 `Session\ClientAccount`，闲置自动退出 `Session\IdleLogout`（分钟数是后端 machine.xml，跟会话一起下来；到点退出以后发 `TimedOut`，
  顶栏 `TopBarViewModel` 接着弹登录框（写明原因、登录名填好，取消当访客；开着一个就不再叠）；`MainViewModel` 记着被藏掉的那一页，重新登录看得到了回去；
  闲没闲看 Windows 的 `GetLastInputInfo`，**别改回 WPF 的 `InputManager.PreProcessInput`**——鼠标停在窗口上时界面一刷新 WPF 就补发鼠标移动，永远不算闲着）。
- EC 目录 `ClientEc`（连上拉全量 + 推送增量，`TryGet("元件全路径.参数名")`）、当前报警 `ClientAlarms`。

## 10. 旧代码里和规则不一致的地方（2026-10-27 对过代码，下面几处都还在；改到时顺手改，新代码别学）

- 裸 TextBox：报警历史、日志历史/实时的关键字框，IoView 搜索框。
- 写死颜色 / 字号：LoadPort、Robot 手动控件里的 `#555`，腔体手动页和 `LoadPortInfoCard` 各还剩一处 `#555`（腔体页别处已换成 `DarkInactiveStatus`），LoadingWindow 的 `#333333`。
- `MainWindow` 有 `Window.Resources`（BooleanToVisibilityConverter）。
- Manual 的 VM 在 Init 里同步等 RPC。
- `.codex\skills\xyz-wpf-mvvm-conventions\SKILL.md` 是给 Codex 用的客户端规矩英文摘要（2026-10-27 照代码改过一遍），跟本 skill 对不上时以本 skill 为准。
