# 客户端（WPF）

路径都相对 `xyz.Core\Client`。.NET 10，WPF 工程 `net10.0-windows`、其余 `net10.0`；Nullable、ImplicitUsings 全开。
MaterialDesignThemes 5（暗色）、CommunityToolkit.Mvvm 8、protobuf-net.Grpc（code-first）、ScottPlot 5、System.Reactive。

## 1. 工程和依赖方向

```
xyz.Client（壳，WinExe）
  → 功能模块 xyz.Client.Main / Alarm / DataCenter / Io / Manual / Recipe / Setting
    → Common\xyz.Client.Presentation（控件、样式、语言包、公共显示模型）
      → Common\xyz.Client.Common（RPC、远程事件、日志、EC/报警缓存、会话）、Common\xyz.Client.DataModels（BaseViewModel）
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
2. `GrpcClientFactory.Initialize()`（默认 `http://localhost:5000`，exe 旁 `client.json` 的 `GrpcAddress` 可改）。
3. 问后端系统设置 `ISystemService.GetSettingsAsync`（3 s 超时，每 1 s 重试，最多等 30 s）→ `L10n.Apply(语言)`；
   返回的模块名、腔体名用来生成 IO / 腔体菜单，LoadPort、机械手名单用来生成主界面的 LoadPort 页签和默认调度图。
   后端一直不在就用 zh-CN、这些按模块生成的东西整次为空（主界面显示提示）；换语言要重启客户端。
4. `RemoteEventBus.Initialize()`、`ClientAlarms.Initialize()`、`ClientEc.Initialize()`。
5. 建 DI：`AddXyzClientServices(settings)`（壳 VM、平台菜单、各功能模块注册）→ `ClientModuleLoader.Load(services)`
   （扫 exe 目录和 `Modules\**\*.dll` 里带 `[ClientModule]` 的 `IClientModule`，按 Key 排序调 `Register`；机型后注册，
   同一个 keyed 页面键会覆盖平台的）→ 机型语言包 `L10n.AddPack(module.PresentationAssembly)` → `IocHelper.ServiceProvider = ...`。
6. 统一调一遍所有 `BaseViewModel` 的 `Init()`（各自 try/catch 记日志）。`MainViewModel` 最先注册，它的 Init 会把所有页面建出来，
   所以**页面的构造先于自己 VM 的 Init**。
7. 主窗口先放到屏幕外 `Show`、等 `ContentRendered`（所有页面套模板、排版、Loaded 一次做完），再挪回来最大化、关加载窗口。

## 3. 菜单和页面

- 菜单写在代码里，不进数据库：平台菜单在 `xyz.Client\Menus\PlatformMenuProvider.cs`；机型真有独有页面时实现 `IClientMenuProvider`。
  `ClientMenu(parentCode, code, sort, title?)`：`parentCode == null` 是一级菜单；**Code 就是页面的 keyed 注册键**；
  显示名取语言包 `menu.{Code}`（没配就显示 Code），按模块生成的菜单直接用 title（模块名）。
- 一级菜单：Main 1、Manual 2、Recipe 3、Alarm 4、DataCenter 5、Setting 6、Io 7。
  Recipe 下：Recipe.Sequence 1（流程配方）、Recipe.Process 2（工艺配方）。
  Setting 下：Setting.Ec 1、Setting.WaferLedger 2、Setting.User 3、Setting.Role 4。
- 页面注册（功能模块的 `ServiceExtensions`）：
  ```csharp
  services.AddSingleton<XxxViewModel>();
  services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<XxxViewModel>());   // 统一 Init 用
  services.AddKeyedSingleton<UserControl, XxxView>("Setting.Xxx");                    // 键 = 菜单 Code
  ```
  按模块一页的（IO、腔体手动）用工厂：`AddKeyedSingleton<UserControl>(code, (sp, key) => new IoView(...))`。
- `xyz.Client\Views\PageHost.cs`：全部页面启动时挂上，当前页 Visible、其余 Hidden。
  **页面常驻：Loaded / Unloaded 只触发一次**。要知道页面显示没显示用 `IsVisibleChanged`，转给 VM（示例 `WaferLedgerView.xaml.cs` →
  `viewModel.SetPageVisible(...)`）；页面不在前台时不拉数据、不跑每帧的东西（TrendChart、Robot 控件隐藏时停 `CompositionTarget.Rendering`）。
- 没注册页面的菜单显示占位页。

### 主界面（菜单 Main，`xyz.Client.Main`，布局照用户 2026-10-04 给的图，见 decisions.md）
- 三块：左 **系统操作**（平台固定：系统状态徽标、当前模式、报警条数，Auto / Manual / Stop / Reset）| 中 **整机调度**（可替换）|
  右 **LoadPort 页签**（`UnderlineTabListBoxStyle`，一个 LoadPort 一个：载具 / 槽图状态 / 槽数、LotID、Sequence 选择框、创建 / 启动 Job、槽位表）。
- **以后机台变了，要动的就是中间和右边，都不用改主界面**：
  - 右边页签照后端系统设置的 `SystemSettingsDto.LoadPorts`（sc.xml 装了的 LoadPort，先后同 sc）生成，3 个、4 个都不改代码。
  - 中间默认是 `Views\DispatchView`：照 `SystemSettingsDto.Robots` 一台机械手一张公共调度图 `DispatchMap`，站点按机械手 sc.xml `Stations`
    的 `Direction` 摆在上下左右，卡片按站点类型（`RobotStationDto.Kind`：LoadPort 花篮卡、腔体 / 其他单片卡）选，sc 里多一个站点图上就多一张。
  - 机型要别的摆法（几台机械手、缓存位、对中台……）：在机型 `IClientModule.Register` 里
    `services.AddKeyedSingleton<UserControl, 自己的View>(ClientViewKeys.MainDispatch)`，后注册的生效，整块换掉；
    自己的图照样可以拿 `DispatchMap`、`StationCard`、`Robot` 控件来拼。主界面由 `MainPageView` 构造时按这个键从 DI 取。
- 系统状态（`ModuleStateBadge` 整条色块）：没连上 / 还没收到设备总状态 = 未连接（灰），有报警 = 报警（红），有模块在动 = 运行中（蓝），
  只有警告 = 警告（黄），否则空闲（绿）。模式取设备总状态 `EquipmentStatusDto.IsAuto`。四个按钮连上且收到状态后才能点。
  Auto / Manual / Stop 走 `IEquipmentService`，Reset 跟右上角一样走 `IAlarmService.ResetAllAsync`；结果写顶栏日志。
- 槽位表（`DenseDataGridStyle`，行高 24，25 槽一屏放下，大号在上）：片以晶圆账为准（`LoadPortDto.LedgerSlots`，账上没登记才按 Mapping）；
  状态列 `JobWaferStateDataGridTextStyle`（物理状态不正常的先显示交叉片 / 叠片 / 状态不明，否则显示工艺状态）。
  Sequence 是界面上选的（点创建 Job 时交给后端）：上面的 Sequence 框一选给全篮能做的片套上，⊕ 弹公共选择弹窗给这一片单独选，⊖ 清空（这片不做）；
  后来才放上 / Mapping 出来的片也套上面选的那个；交叉片、叠片、状态不明的没有 ⊕ ⊖。
- **创建 / 启动 Job**（2026-10-05 接上）：订 Job 全貌推送（`JobListDto`，token "Job"，留存），每个页签认自己 LoadPort 上没删的 CJ（`LoadPortJobModel.ControlJob`），
  断开时清掉。创建 Job = `IJobService.CreateAsync`（LoadPort、LotID、选了 Sequence 的槽、操作人；启动方式由设备 SC 决定，后端跟 Host 一样先建 PJ 再建 CJ），有要做的片、这个 LoadPort 上没 CJ 才能点；
  启动 Job = `ControlJobCommandAsync`（CJStart），Auto 下、CJ 是 WAITINGFORSTART（`ControlJobDto.StateWaitingForStart`）才能点。结果写顶栏日志。
  Job 的列表、详情、暂停 / 停止 / 中止这些按钮还没有界面（要先给参考图或出样稿）。

### 加一个页面（清单）
1. 功能模块里加 `Models\`、`ViewModels\XxxViewModel.cs`、`Views\XxxView.xaml(.cs)`。
2. 模块 `ServiceExtensions` 注册 VM（+ BaseViewModel 别名）和 keyed View。
3. `PlatformMenuProvider` 加 `ClientMenu`（注意后面的 sort 顺延）。
4. 两个语言包加 `menu.{Code}` 和页面文字。
5. 样式缺的加到 `Presentation\Styles`，不在页面里写资源。
6. 编译；用离屏预览（见 machine-and-tools.md）出中英文截图看一遍。

## 4. ViewModel

- 继承 `xyz.Client.DataModels.ViewModels.BaseViewModel`；不用 `[ObservableProperty]`、`[RelayCommand]` 这类源生成器。
- 文件布局：常量（`private const` / `static readonly`）→ `#region Column` → `#region Command` → `#region Service` → 构造 → `Init()` → 其他方法。
  - Column：**私有字段紧挨在它的属性上面**，属性用 `SetProperty`；派生属性通知、命令刷新（`NotifyCanExecuteChanged`）写在 setter 里。
  - Command：`IRelayCommand` / `IAsyncRelayCommand(<T>)` 属性，在构造里 `new`。
  - Service：只放字段（服务代理、计时器、订阅句柄、私有状态），不放方法。
- 构造里只建对象（集合、命令、服务代理 `GrpcClientFactory.Create<IXxxService>()`、计时器、CollectionView），**不拉数据**。
- 拉数据、订阅事件放 `Init()`（由壳统一调，View 里不调）。Init 要能重复调：先 `Dispose` 旧订阅、`-=` 再 `+=`。
- 命令方法 `Do` 开头（`DoMove`、`DoConfirm`）；不是命令的方法不加 Do。会走 RPC / IO 的命令用 `AsyncRelayCommand`，方法返回 `Task`。
- 不在 `Init` 里同步等 RPC（`.GetAwaiter().GetResult()`）——旧代码有（Manual、User、Role），新代码不要学。

### 调后端
```csharp
var response = await _service.MoveAsync(request);
if (!response.Success)
{
    string reason = string.IsNullOrEmpty(response.Code) ? response.Message : L10n.Get(response.Code, response.Args);
    ClientLog.Error(LogModule, L10n.Get("setting.ledger.move_failed", reason));
    return;
}
var data = response.DeserializeData<XxxDto>();   // 失败会抛 InvalidOperationException
```
- 错误显示一律"错误码 → 语言包"；没码的老接口才用 `Message`。
- 结果提示走顶栏日志栏：`ClientLog.Info/Warn/Error(模块名, 句子)`。不弹 MessageBox（只有 User/Role 旧代码还有）。

### 事件
- `EventBus.Register<TDto>(TDto.EventToken 或模块名, handler)`：远程消息由 `RemoteEventBus` 投递到 **UI 线程**，handler 里直接改绑定属性；
  返回的 `IDisposable` 存在字段里，Init 重复调时先 Dispose。留存消息（retain）订上就会补发最后一条。
- 连接状态：`RemoteEventBus.ConnectionChanged -= On; += On;` 再 `On(RemoteEventBus.IsConnected)`；连上时重拉全量（断线期间漏了推送）。
- 高频推送、查询请求用 Rx：`Throttle` / `Sample` + `Select(Observable.FromAsync(...))` + `Switch()` + `ObserveOn(SynchronizationContext)`（DataChart、RealChart）。
- 后端推的是"变了"通知、界面自己重拉时，要攒一下再拉（DispatcherTimer 节流，见 WaferLedgerViewModel.RequestReload）。

## 5. View

- 构造里只做 `InitializeComponent(); DataContext = IocHelper.GetRequiredService<XxxViewModel>();`，不调 Init；
  需要感知可见性时再挂一个 `IsVisibleChanged`。按模块一页的 View 由工厂把 VM 传进构造（IoView）。
- 自带 VM 的复用控件（LogBar、AlarmBar、手动页的 ChamberManualControl 等）自己建 VM、自己 Init，这是例外，不是页面的做法。
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

- 文件：Border / Button / Card / ComboBox / ContextMenu / DataGrid / DatePicker / ListBox / TextBlock / TextBox / TreeView `Styles.xaml`，
  加 `Color.xaml`（老调色板，含 `Wafer*` 晶圆色）、`DarkColors.xaml`（暗色主题 token）、`FontSize.xaml`（Size10~Size28）。
- 合并在 `xyz.Client\App.xaml`，**顺序有意义**：后面的样式用 `StaticResource` 取前面的颜色、字号。新加样式文件要同时加进 App.xaml
  和各模块 `Properties\DesignTimeResources.xaml`（设计时用，各模块的列表目前不全）。`Theme\MaterialDesignTheme.xaml` 没人用。
- 命名：`{档位}{控件}Style`——档位 Default / Toolbar / Compact / Dense / Mini / Large，再加 Danger / Success / Outlined 变体
  （`ToolbarDangerButtonStyle`、`CompactTextBoxStyle`）；专用的叫 `{用途}{元素}Style`（`TopBarInfoBorderStyle`、`DialogCardBorderStyle`）。
- 高度档：默认输入 40 / 默认按钮 44 / 工具栏 34 / 紧凑按钮 32 / 紧凑输入 28 / Mini 22。
- 颜色 token（DarkColors）：底 `DarkWindowBackground`→`DarkSurfaceBackground`→`DarkSurfaceVariantBackground`→`DarkControlBackground`、
  边 `DarkBorderBrush`、字 `DarkPrimaryText` / `DarkDisabledText`、强调 `DarkAccent` / `DarkAccentPressed` / `DarkAccentSoft`、
  状态 `DarkRunningStatus` 绿 / `DarkWarningStatus` 黄 / `DarkAlarmStatus` 红 / `DarkCommunicationStatus` 蓝 / `DarkInactiveStatus` 灰、
  悬停 `DarkHoverBackground`、遮罩 `DarkOverlayBackground`。新颜色先加 token 再用。
- Tag 驱动的公共样式（数据绑到 `Tag`，样式按值换外观）：`ShowWhenTagTrueStyle`（FrameworkElement，Tag=True 才显示）、
  `NullContentHintTextStyle`（Tag 为 null 才显示，做占位提示）、`PageOverlayBorderStyle`、`StatusDotBorderStyle`、
  `TopBarStackAccentButtonStyle`（有报警变红）、`WaferBarStyle`（Tag = 晶圆色调 Idle/InProcess/Completed/Error/Crossed/Double/Dummy/Unknown，其他=空槽虚线）、
  `AlarmCountTextStyle`（Tag=True 红字，报警条数）、`MapStatusTextStyle`（Tag = CarrierSlotMapStatus，写"映射完成"这类字并换色）。
  DataTrigger 的 `Value="Move"` 这种字符串能直接匹配枚举。
- 表单一行"标签 + 值"（主界面系统操作、LoadPort 栏）：`FormLabelTextStyle` / `FormValueTextStyle`。表格行里的小图标按钮（⊕ ⊖）：`RowIconButtonStyle`。
- 需要行数据带特定属性的样式：`LogLevelDataGridTextStyle`（Level）、`PickDataGridRowStyle` / `PickDataGridCellStyle`（IsPickable）、
  `FreshDataGridCellStyle`（IsFresh）、`JobWaferStateDataGridTextStyle`（WaferState，主界面槽位表的状态列）。
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
  `DataType` Text/Integer/Decimal；`Minimum`/`Maximum`/`Unit` 只对数字；`EcKey="组件全路径.参数名"` 从 EC 取格式、范围、单位
  （界面上写的优先）；边输边查格式，回车/失焦提交时查范围；错了红框提示、Value 保留上次合法值；
  发送类按钮绑 `HasError`（`Mode=OneWayToSource`）错着不发；表格行里加 `materialDesign:ValidationAssist.UsePopup="True"`。
  没写 Style 时自动套 `TextBoxStyles.xaml` 里按类型的隐式样式（= `DefaultTextBoxStyle`）。**自定义控件的默认样式都这么给，别在构造函数里
  `SetResourceReference(StyleProperty, ...)`**：那是本地值，列表行、表格行模板里写的 `Style` 优先级比它低，写了不生效
  （2026-10-04 改的：之前 IO 页 AO 设定框、EC 页值框写的 `CompactTextBoxStyle` 一直没生效，实际是 40 高、上下各 4 的默认样式）。
- **DateTimePicker** + `QueryDateRange.Of/Today/LastDays/LastHours`：查时间段，分钟精度，截止那一分钟算进去。
- **TrendChart**（ScottPlot 5 封装）：喂 `ObservableCollection<TrendSeries>`，只用 `SetData` / `Append` 改数据；单一左 Y 轴，所有曲线按真实值画。
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
- LogBar / AlarmBar 只在主窗口顶栏用。
- **DispatchMap**（调度图，Robot 手动页左边和主界面中间共用）：给 `RobotName`（机械手模块名）就自己建 `DispatchMapViewModel`、订机械手和各站点的推送
  （都是留存消息，不用另外拉）；机械手在中间，站点按它 sc.xml `Stations` 的方向摆四周，每个站点一张 `StationCard`
  （`Kind` = LoadPort 用 `LoadPortInfoCard` 花篮卡，其他用 `ChamberInfoCard` 单片卡，用模板切、只建用得上的那张）。
  `CardWidth` / `CardHeight`（默认 280×233）、`RobotHeight`（默认 360）可设，卡片圆片直径跟着 RobotHeight 算（= 叉上的片）；
  一排放不下时那一排整体缩小（只缩不放），不裁站点。LoadPort 卡底栏六盏灯跟 `LoadPortDto` 走（通讯 / 在位 / 到位 / 报警 / 自动 / 手动），
  腔体卡的"配方"取 `ChamberDto.Recipe`。`ChamberInfoCard` 的五行字段哪一行没有值就整行不显示（2026-10-04 起，后端还没给步骤、时间这些）。
- **HoldButton**（按住类按钮，点动用）：按下发 `PressCommand`，按住期间每 `RenewMilliseconds`（默认 200）发一次 `RenewCommand`，
  松开 / 拖出按钮 / 禁用 / 藏起来发 `ReleaseCommand`，三个命令共用 `CommandParameter`；不用 `Command`。样式照普通按钮套。
- **三维硬件**（`Controls\ThreeD`，说明见那儿的 README）：Arm / Lift / FluidPipe / Bowl / HomeCup / Door / Disk / ChamberBase 组件，
  `ChamberScene` 把它们装成腔体手动页的三维图（`Parts` 绑 `ChamberPartsModel`、`Wafer` 绑晶圆账的片），视角工具栏在图下面。
  三维对象不在逻辑树里，颜色用 `DarkHardware*` token（XAML 里 StaticResource、代码里 TryFindResource），不注册逐帧事件。
- **腔体手动页**（`xyz.Client.Manual\Views\ChamberManualControl`，布局照用户给的参考图，见 decisions.md）：部件都来自后端的
  `ChamberPartsDto`（强类型：轴表、气缸表、喷嘴表，门 / Bowl / Lift 的角色、喷嘴在哪条臂后端认好了）——`ChamberPartsModel.Update` 按 Kind / Role / Arm 挑出三维要画的门 / Bowl / 卡盘 / 摆臂，
  VM 拿 `Axes` 做轴页签（`AxisPartModel`）、`Cylinders` 做气缸表（`CylinderPartModel`），都按 sc 的先后。界面不再抄后端的属性名、方法名（2026-10-10 删了 PartValueNames / PartActionNames / PartKinds）。
  动作调 `IChamberService` 的具体方法（`AxisHomeAsync` / `AxisMoveAsync` / `AxisStepAsync` / `AxisJogAsync` / `AxisStopAsync` / `AxisResetAsync`、`CylinderUpAsync` / `CylinderDownAsync`）；
  点动用 HoldButton：按下 Jog、续 `AxisJogRenewAsync`、松手先等点动请求回来再发 Stop。轴参数（移动速度、点动速度、步距）默认值取这根轴的 EC，`ClientEc` 拉到后补上空着的。

## 8. 语言包（`Common\xyz.Client.Presentation\Localization`）

- `Strings.zh-CN.xaml`、`Strings.en-US.xaml`，`<sys:String x:Key="...">`，**两份的 key 必须一一对应**（加一条就两边都加）。
- **界面上显示的都要多语言**（标题、按钮、状态、提示、报错，用户 2026-10-04 定的）；从数据库、配置查出来的数据
  （sc 里的模块 / 部件名、配方名、日志内容、片号）原样显示，不翻。后端报错参数里带的固定标识也要换成叫法再显示，
  比如部件动作名（Shared 枚举 `ChamberPartAction`，如 ValveOn）走 `PartActionText.Of` → `part.action.{转小写下划线}`，中文界面不露英文。
- key 小写点分、叶子 snake_case：错误码（= `xyz.Shared\Errors\ErrorCodes.cs` 的常量值，如 `wafer.slot_occupied`）、`module.state.*`、
  `common.*`、`shell.*`、`setting.ledger.*`、`menu.{Code}`（Code 保留大小写）；枚举值做叶子时保留原样（`setting.ledger.process.InProcess`）；提示文字 `_tip` 结尾。
- XAML 用 `{DynamicResource key}`；C# 用 `L10n.Get(key, 参数...)`（`string.Format`，可写 `{0:00}`）、`L10n.Get(code, response.Args)`；没有这个 key 时返回 key 本身。
- 机型独有的文字放机型的 `*.Client.Presentation\Localization\Strings.{lang}.xaml`；框架页面（包括机型挂的框架菜单）的文字放平台包。
- 界面语言由后端 sc.xml `System` 节点的 `Language` 决定，客户端启动时问一次。

## 9. 显示模型

- 公共的在 `Presentation\Models`（`ModuleStates`、`ModuleStateTone`、`QueryDateRange`、`TrendSeries`、`InputDataType`、`ProcessRecipeOptionModel`、
  `RobotModel` / `RobotStationModel`（机械手和它的站点，Robot 手动页、调度图共用）、`StationWafers`（LoadPort 花篮 / 腔体片位 → 圆片，以晶圆账为准）……），
  页面自己的在功能模块 `Models\`。
- DTO → 模型：简单的 `dto.Adapt<T>()`（Mapster，整体替换）；要保住实例的（动画、选中状态）写 `Update(dto)` 就地改；也可以构造里手写映射。
- 当前用户 `xyz.Client.Common.Session.ClientSession.UserName`（登录没做，先固定 Admin），要记操作人的地方都从这儿取。
- EC 目录 `ClientEc`（连上拉全量 + 推送增量，`TryGet("组件全路径.参数名")`）、当前报警 `ClientAlarms`。

## 10. 旧代码里和规则不一致的地方（截至 2026-10-03；改到时顺手改，新代码别学）

- 裸 TextBox：报警历史、日志历史/实时的关键字框，IoView 搜索框，两个对话框。
- 写死颜色 / 字号：LoadPort、Robot 手动控件里的 `#555`（腔体页已换成 `DarkInactiveStatus`）、LoadingWindow 的 `#333333`、Dialogs 的 `FontSize="14"`。
- `MainWindow` 有 `Window.Resources`（BooleanToVisibilityConverter）。
- Manual、User、Role 的 VM 在 Init 里同步等 RPC；User/Role 用了 MessageBox。
- User/Role 选中项变了没刷新删除按钮的 CanExecute（删除按钮可能一直灰）。
- `.codex\skills\xyz-wpf-mvvm-conventions\SKILL.md` 里有过时内容（路径写成 D:\Common、提到 RpcClient/IRpcService、xyz.Shared/Models），以本 skill 为准。
