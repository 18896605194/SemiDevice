import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.argv[2] || 'D:/Code');
const out = path.join(root, 'docs/frontend-controls');
const catalog = JSON.parse(fs.readFileSync(path.join(out, 'catalog.json'), 'utf8'));
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const ref = (file,label) => `<a class="ref" data-f="${esc(file)}">${esc(label || path.basename(file))}</a>`;
const controls = catalog.controls;
const byName = Object.fromEntries(controls.map(c => [c.name,c]));
const note = {
 InputTextBox:['统一文本、整数和小数输入。业务页面绑定 Value；Text 是输入中的草稿。', 'Value 默认双向绑定。格式错误立即提示，范围在 Enter 或失焦提交时检查；错误输入不覆盖上次合法 Value。EcKey 可从 EC 目录取得类型、上下限、单位，显式设置优先。下发动作前检查 HasError。', {Value:'{Binding TargetFlow}',DataType:'Decimal',Minimum:'0',Maximum:'100',Unit:'L/min',HasError:'{Binding FlowHasError, Mode=OneWayToSource}'}],
 DateTimePicker:['分钟精度的日期 + 小时 + 分钟选择器。', '绑定 DateTime? Value，清空日期得到 null。秒归零；查询区间的业务处理仍由调用方负责。', {Value:'{Binding StartTime}',Hint:'{DynamicResource common.start_time}'}],
 PickerBox:['从库中选择一个对象，点击省略号打开公共选择弹窗。', 'ItemsSource 给候选数据，Columns 是普通集合属性，ValuePath 指定回填字段，默认 Name。Value 默认双向绑定。IsEditable=true 才允许手输；HasError 由页面校验设置。', {Value:'{Binding RecipeName}',ItemsSource:'{Binding Recipes}',ValuePath:'Name',PickerTitle:'选择配方',Placeholder:'请选择'}],
 HoldButton:['按住执行、松开停止的点动按钮。', 'PressCommand 和 ReleaseCommand 分别绑定开始与停止。不要只处理 Click；失去捕获、禁用等释放路径由控件处理，真实设备联锁仍在服务端。', {Content:'按住点动',PressCommand:'{Binding JogStartCommand}',ReleaseCommand:'{Binding JogStopCommand}'}],
 SignalLamp:['一个传感器点位的一盏灯和名称。', 'IsActive 是 bool?：true 亮、false 灭、null 未知；IsAlarm=true 时触发显示报警色。它是独立控件，不属于 Tank 的几何模型；归属、摆放和数据绑定由组合层决定。', {Text:'Tank1.H',IsActive:'{Binding Tank.H.IsActive}',IsAlarm:'False'}],
 SignalTower:['红、黄、绿、蓝四盏独立的状态灯。', '四个开关分别绑定，不相互排斥，也不自动控制硬件灯塔。', {IsRedOn:'{Binding HasAlarm}',IsYellowOn:'{Binding HasWarning}',IsGreenOn:'{Binding IsRunning}',IsBlueOn:'{Binding IsConnected}'}],
 TrendChart:['统一时间趋势图，复用现有 ScottPlot 封装。', 'Series 接收 TrendSeries 集合；Xs 用升序 OADate，Ys 与之等长，NaN 表示缺测。使用 SetData / Append 通知刷新。FollowRange 与可见区间、游标时刻可双向绑定；所有曲线共用左侧纵轴，数字量按 0/1 显示。', {Series:'{Binding Series}',RangeStart:'{Binding StartTime}',RangeEnd:'{Binding EndTime}',FollowRange:'{Binding FollowRange}',VisibleStart:'{Binding VisibleStart}',VisibleEnd:'{Binding VisibleEnd}',CursorTime:'{Binding CursorTime}'}],
 Wafer:['二维圆片，按晶圆账中的状态和片号显示。', 'Data 通常接 WaferModel，显示 LpSlot 和 State。IsDiskVisible 允许无片数据时仍画圆盘。RotationSpeed 是度/秒，超过 1 才转，不能直接当作设备 RPM。Label 虽已声明，当前二维模板仍显示 Data.LpSlot。', {Data:'{Binding Wafer}',IsDiskVisible:'True',RotationSpeed:'{Binding DisplayDegreesPerSecond}'}],
 FluidPipe:['二维布局中的三维透明管路控件，自带视口。', 'Points 使用画布坐标；管径、管壁和圆角也使用画布单位，内部按 0.01 换算到三维场景。IsFilled 是液芯显示，IsFlowing 是流动显示，IsAnimationEnabled 只暂停动画。拓扑必须由布局提供，控件不能从阀门数量猜出管路。', {Points:'{Binding PipePoints}',PipeThickness:'16',WallThickness:'1.6',BendRadius:'18',IsFilled:'True',IsFlowing:'{Binding IsFlowing}'}],
 Robot:['二维机械手，直接绑定各手臂数据。', 'Arms 接 RobotArmModel 集合，每只手臂独立 Extension（0~1）、Heading 和 Wafer。Rotation 是 RobotDirection 枚举，仅接受 North / East / South / West；Travel 是平移。控件执行先收臂、再转向/平移、再伸出的视觉顺序，不发送设备运动指令。', {ArmType:'Linear',Arms:'{Binding Arms}',Rotation:'{Binding Rotation}',Travel:'{Binding Travel}',IsAnimationEnabled:'True'}],
 Robot3D:['自带相机、灯光和视角操作的三维机械手。', '继承 RobotViewBase，与二维 Robot 共用 Arms、Rotation、Travel 等输入。可拖动旋转、滚轮缩放；ResetView / ShowTopView 是普通方法。只显示，不订阅或发送设备动作。', {Arms:'{Binding Arms}',Rotation:'{Binding Rotation}',Travel:'{Binding Travel}',ShowViewToolbar:'True'}],
 Buffer:['带标题和模块状态的缓存架组合卡片。', 'FirstSlot 表示物理起始槽号，SlotCount 来自装机，Wafers 按 Slot 放片。WaferCount 是只读输出。需要纯场景时选 BufferView，共享视口时选 BufferVisual3D。', {Title:'Buffer1',FirstSlot:'1',SlotCount:'{Binding SlotCount}',Wafers:'{Binding Wafers}',StateText:'{Binding StateText}',IsProduction:'{Binding IsProduction}'}],
 BufferView:['自带视口和视角交互的缓存架。', '使用与 BufferVisual3D 相同的槽位数据。WaferCount 为只读依赖属性，绑定输出不能从外部 SetValue。', {FirstSlot:'1',SlotCount:'{Binding SlotCount}',Wafers:'{Binding Wafers}'}],
 ArmVisual3D:['摆臂几何件，零角度沿 +X，局部 Y 向上。', 'Angle 使用度。Length 是回转中心到末端安装块中心的长度。Attachments 是普通 Visual3DCollection，不是依赖属性；喷嘴管路挂入后随摆角转动，喷液状态仍独立。Lift 也必须单独组合。', {Angle:'{Binding DataContext.ArmAngle, ElementName=Scene}',Length:'2.2'}],
 BowlVisual3D:['独立升降的 Bowl 薄壁圆环。', 'IsRaised 表示上/下到位；IsUnknown=true 时显示未知位置并变红。HeightLevel=1/2/3 是外观高度规格，不是 Bowl 数量或升降挡位。35021 每腔当前只有一个 Bowl1，不因控件支持高度等级而增加硬件。', {IsRaised:'{Binding DataContext.BowlRaised, ElementName=Scene}',IsUnknown:'{Binding DataContext.BowlUnknown, ElementName=Scene}',HeightLevel:'1'}],
 LiftVisual3D:['独立气缸升降件，供摆臂等组件安装。', 'IsRaised / IsUnknown 来自反馈。MountHeight 是只读依赖属性，表示局部安装面当前 Y 高度，能用于附件平移绑定；不包含外部 Transform，不能重复叠加安装位移。', {IsRaised:'{Binding DataContext.LiftRaised, ElementName=Scene}',IsUnknown:'{Binding DataContext.LiftUnknown, ElementName=Scene}'}],
 TankVisual3D:['纯槽体三维几何，透明圆筒和液体。', 'FillFraction 接 0~1 的显示比例；槽的离散液位必须由调用方换算，不能声称是实测百分比。IsAlarm 控制液体报警色。这里没有传感器灯和管路，它们应独立组合。', {FillFraction:'{Binding DataContext.DisplayFillFraction, ElementName=Scene}',IsAlarm:'{Binding DataContext.TankAlarm, ElementName=Scene}'}],
 TankScene:['带相机、液位文字及信号灯的槽组合视图。', 'Tank 接 TankModel。调用 TankModel.Update(TankDto) 更新；DTO 中没有配置的信号为 null，显示模型 IsPresent=false，不画该灯。要自由摆放每个 Sensor 时可使用 TankVisual3D + 独立 SignalLamp，而不是固定组合视图。', {Tank:'{Binding Tank}',WallTransparency:'0.72'}],
 FluidRouteVisual3D:['共享视口中按任意三维路径绘制的透明管路。', 'Path 是 Point3DCollection，坐标使用场景单位。PipeRadius 是外半径，WallThickness 必须留出内腔。FlowSpeed 是场景单位/秒，ReverseFlow 改变动画方向。路径及端点由布局指定，液芯与流动显示单独控制。', {Path:'{Binding DataContext.Route3D, ElementName=Scene}',PipeRadius:'0.08',WallThickness:'0.016',BendRadius:'0.18',IsFlowing:'{Binding DataContext.IsFlowing, ElementName=Scene}'}],
 FluidPipeVisual3D:['沿摆臂布置的专用喷液管路，可显示喷口液柱。', 'Length 与摆臂长度配套。StreamLength=0 只保留管内显示；大于 0 显示喷口到接液面的液柱。一般挂进 ArmVisual3D.Attachments，喷液开关绑定独立喷嘴反馈。它与任意路径的 FluidRouteVisual3D 用途不同。', {Length:'2.2',StreamLength:'0.6',IsFlowing:'{Binding DataContext.NozzleOn, ElementName=Scene}'}],
 DoorVisual3D:['独立腔门外观，显示开、关和未知位置。', 'IsOpen 表示开到位；IsUnknown 优先显示行程中间和报警色。Width / Height 是几何尺寸，不是普通 WPF 布局尺寸。只显示，不发 DO。', {IsOpen:'{Binding DataContext.DoorOpened, ElementName=Scene}',IsUnknown:'{Binding DataContext.DoorUnknown, ElementName=Scene}'}],
 DiskVisual3D:['三维圆盘与晶圆，复用二维 Wafer 的状态数据。', 'Data 接同一份 WaferModel。Label 非空时覆盖文字，二维 Wafer 当前不做这项覆盖。RotationSpeed 为度/秒；Radius、Thickness、SpindleHeight 为场景单位。与 Bowl、SpinMotor 数据分开绑定。', {Data:'{Binding DataContext.Wafer, ElementName=Scene}',IsDiskVisible:'True',Radius:'1.32',Thickness:'0.045'}],
 HomeCupVisual3D:['摆臂 Home 位接液杯。', 'Radius / Height 控制杯的几何尺寸。IsDraining 只控制接液显示；是否真的有杯、装在哪、与哪条喷嘴对应，由装机与布局组合层决定。', {Radius:'0.28',Height:'0.35',IsDraining:'{Binding DataContext.HomeDraining, ElementName=Scene}'}],
 ChamberBaseVisual3D:['腔体设备的公共安装底板。', 'Length / Width / Thickness 使用场景单位。Attachments 是普通集合，放入 Bowl、门、卡盘、摆臂总成等；共用父变换只用于安装位置，不改变各组件反馈。', {Length:'6.4',Width:'4.2',Thickness:'0.12'}],
 CarrierVisual3D:['载具花篮几何，可与 LoadPort 共享视口。', 'SlotCount 必须给实际槽数，默认 0 不画。Wafers 按 WaferModel.Slot 放片；整个替换集合触发刷新。SelectedSlot 控制选中槽，SlotOf 是用于命中识别的普通方法。放入 LoadPortVisual3D.Attachments 可随托板移动。', {SlotCount:'{Binding DataContext.SlotCount, ElementName=Scene}',Wafers:'{Binding DataContext.Wafers, ElementName=Scene}',SelectedSlot:'{Binding DataContext.SelectedSlot, ElementName=Scene}'}],
 BufferVisual3D:['贯通缓存架几何件，供共享视口组合。', 'FirstSlot / SlotCount 决定物理槽范围，Wafers 按 Slot 放片。支持集合及单片槽位变化，在 UI 线程更新。WaferCount 为只读依赖属性。', {FirstSlot:'1',SlotCount:'{Binding DataContext.SlotCount, ElementName=Scene}',Wafers:'{Binding DataContext.Wafers, ElementName=Scene}'}],
 LoadPortVisual3D:['LoadPort 机身、托板和门的独立三维外观。', 'IsDocked 与门开、门未知分别绑定。IsPresent / IsPlaced / IsAutoMode 是面板显示信号。载具独立挂在 Attachments；该控件本身不拉服务数据。', {IsDocked:'{Binding DataContext.Docked, ElementName=Scene}',IsDoorOpen:'{Binding DataContext.DoorOpen, ElementName=Scene}',IsDoorUnknown:'{Binding DataContext.DoorUnknown, ElementName=Scene}'}],
 LoadPortScene:['LoadPort + 载具的组合视图，自带相机和选片。', '在位和到位都为真才放载具。给 SlotCount、Wafers 以及对接/门/面板状态即可。晶圆来自晶圆账，选片不会创建或删除晶圆。', {SlotCount:'{Binding SlotCount}',Wafers:'{Binding Wafers}',IsPresent:'{Binding Present}',IsPlaced:'{Binding Placed}',IsDocked:'{Binding Docked}',IsDoorOpen:'{Binding DoorOpen}'}],
 RobotVisual3D:['共享视口中的机械手底层几何件。', '它没有 Arms / Rotation 依赖属性；姿态通过普通 SetPose(ArmType, rotation, travel, ArmVisuals) 输入，运动解算由 RobotViewBase 提供。一般页面直接选 Robot3D；只有需要共享视口时才用这一层。', {}],
 ChamberScene:['平台原有的腔体组合视图。', 'Devices 接 ChamberDeviceDataModel，先 Update(ChamberDeviceDataDto) 再显示；Wafer 独立来自晶圆账。平台固定装配只使用第一个 Bowl；项目按布局装配使用下方的 35021 ChamberView。', {Devices:'{Binding Devices}',Wafer:'{Binding Wafer}'}],
 ChamberView:['35021 项目的腔体显示控件，消费 ToolBuilder 布局。', 'Snapshot 给完整实际设备清单，Layout 只覆盖位置和挂载。VisualCatalog 使项目自有外观优先、平台外观补齐。它属于项目 Presentation，不是平台通用控件。', {Snapshot:'{Binding Snapshot}',Layout:'{Binding Layout}',VisualCatalog:'{Binding VisualCatalog}',Wafer:'{Binding Wafer}'}],
 ChamberInfoCard:['腔体状态信息卡片，标题与圆片分开显示。', '状态文字和色调分别绑定；Wafer 接当前位置晶圆数据，Recipe / Step / 时间仅显示。Number 是站点编号，不会自行定位后端模块。', {Title:'Chamber1',Number:'1',StateText:'{Binding StateText}',StateTone:'{Binding StateTone}',Wafer:'{Binding Wafer}',Recipe:'{Binding Recipe}'}],
 BottomChamberInfoCard:['标题在底部的腔体信息卡片。', '此版本使用字符串 State / IsOnline 等显示字段，与 ChamberInfoCard 的 StateText / StateTone 接口不同，替换控件时需核对属性表。', {Title:'Chamber1',State:'{Binding StateText}',IsOnline:'{Binding ConnectionText}',Wafer:'{Binding Wafer}',Recipe:'{Binding Recipe}'}],
 LoadPortInfoCard:['载具、片数和模块状态的 LoadPort 信息卡片。', 'SlotCount / Wafers 提供实际槽位数据。Present、Placed、Alarm、AutoMode 与通信状态分别绑定；缺少某项反馈不能用别的状态代替。', {Title:'LoadPort1',SlotCount:'{Binding SlotCount}',Wafers:'{Binding Wafers}',Present:'{Binding Present}',Placed:'{Binding Placed}',StateText:'{Binding StateText}',StateTone:'{Binding StateTone}'}],
 StationCard:['调度图按站点类型选择的卡片包装。', 'DataContext 必须是 RobotStationModel。它据站点类型选择 LoadPortInfoCard 或 ChamberInfoCard；DiskSize 统一片的显示尺寸。', {DataContext:'{Binding Station}',DiskSize:'100'}],
 DispatchMap:['按机械手站点表自动组合的调度图。', 'RobotName 是模块名和订阅 token；给值会建立内部显示模型、订阅推送。静态文档展示离线空态，不伪造站点。CardWidth / CardHeight / RobotHeight 调整显示，DiskSize 是只读计算结果。', {RobotName:'Robot1',CardWidth:'280',CardHeight:'233',RobotHeight:'360'}],
 ModuleChip:['一个模块名和状态圆点，边框表示生产/维护。', 'Text、Tone、IsProduction 独立提供。HasMode=false 不画生产/维护边框，适用于不区分此模式的模块。', {Text:'Chamber1',Tone:'{Binding StateTone}',IsProduction:'{Binding IsProduction}',HasMode:'True'}],
 ModuleStateBadge:['模块状态文字徽章。', 'Text 给文字、Tone 给色调，IsCompact 控制紧凑外观；它与模块服务没有耦合。', {Text:'{Binding StateText}',Tone:'{Binding StateTone}',IsCompact:'False'}],
 ModuleStatusBar:['主界面顶部的模块状态行。', '当前控件内部创建 ModuleStatusBarViewModel 并订阅整机状态；没有公开自定义依赖属性。离线时没有真实模块列表，文档画面使用明确的样例数据。', {}],
 AlarmBar:['客户端当前报警的顶部摘要栏。', '内部使用 AlarmBarViewModel / ClientAlarms；没有公开自定义依赖属性。装入应用共享资源后声明即可，报警清单由客户端状态提供。文档展示无报警态。', {}],
 LogBar:['客户端日志摘要栏。', '内部持有 LogViewModel；没有公开自定义依赖属性。显示数据来自客户端日志链路，文档中不连接真实后台。', {}],
 ProgressOverlay:['覆盖当前页面的进度与取消层。', 'State 接 ProgressState；VM 使用 RunAsync 执行业务任务，并更新进度。ShowDelay 默认 500ms，快速任务不弹；Text 给语言包提示，IsIndeterminate=true 表示不知道进度。放在根 Grid 最后一层覆盖内容。', {State:'{Binding Progress}',Text:'正在加载',IsIndeterminate:'True'}],
 Foup:['FOUP 载具外形示意。', '当前没有自定义依赖属性，使用 Width / Height 等 WPF 布局属性控制大小。它不是按真实槽位数据装配的 CarrierVisual3D。', {Width:'240',Height:'280'}],
 SampleUserControl:['控件模板示例。', '当前只初始化模板，没有业务依赖属性，也不是已实现设备控件。新业务控件应根据实际职责命名。', {}]
};
const groupOf = c => c.project ? '项目控件' : c.namespace.endsWith('.ThreeD') && c.name.endsWith('Visual3D') ? '3D 组件' : c.is3D ? '3D 组合视图' : ['InputTextBox','PickerBox','DateTimePicker','HoldButton'].includes(c.name) ? '输入与操作' : ['SignalLamp','SignalTower','ModuleChip','ModuleStateBadge','AlarmBar','LogBar','ModuleStatusBar','ProgressOverlay'].includes(c.name) ? '信号与状态' : '数据与设备卡片';
const typeLink = c => '#fe-control-' + c.name;
const prefix = c => c.project ? 'project' : c.namespace.endsWith('.ThreeD') ? 'hardware' : 'controls';
function example(c) {
 const attrs = Object.entries(note[c.name]?.[2] || {}).map(([k,v]) => `    ${k}="${v}"`).join('\n');
 let tag = `<${prefix(c)}:${c.name}${attrs ? '\n'+attrs : ''} />`;
 if(c.name==='PickerBox') tag=tag.replace(' />','>')+'\n    <controls:PickerBox.Columns>\n        <controls:PickerColumn HeaderKey="common.name" Path="Name" />\n    </controls:PickerBox.Columns>\n</controls:PickerBox>';
 if (c.namespace.endsWith('.ThreeD') && c.name.endsWith('Visual3D')) return `<Grid x:Name="Scene">\n    <!-- Visual3D 不继承 DataContext，使用 ElementName 明确绑定来源。 -->\n    <Viewport3D>\n        <Viewport3D.Camera>\n            <OrthographicCamera Position="-7,5,9" LookDirection="7,-5,-9"\n                                UpDirection="0,1,0" Width="8" />\n        </Viewport3D.Camera>\n        <ModelVisual3D>\n            <ModelVisual3D.Content>\n                <AmbientLight Color="#A0A0A0" />\n            </ModelVisual3D.Content>\n        </ModelVisual3D>\n${tag.split('\n').map(l=>'        '+l).join('\n')}\n    </Viewport3D>\n</Grid>`;
 return tag;
}
const resources = `<ResourceDictionary.MergedDictionaries>\n    <!-- 先沿用应用 App.xaml 中的 MaterialDesign 与平台共享资源。 -->\n    <ResourceDictionary Source="pack://application:,,,/xyz.Client.Presentation;component/Styles/DarkColors.xaml" />\n    <ResourceDictionary Source="pack://application:,,,/xyz.Client.Presentation;component/Styles/FontSize.xaml" />\n    <!-- 本例不是全部资源清单；完整顺序以应用 App.xaml 为准。 -->\n</ResourceDictionary.MergedDictionaries>`;
const extra = {
 TankScene:['xyz.Core/Client/Common/xyz.Client.Presentation/Models/TankModel.cs','对应的显示模型（在 UI 线程刷新）',`public TankModel Tank { get; } = new();\n\n// dto 由调用方提供，控件不自己查 PLC。\nvoid ApplyTank(TankDto dto)\n{\n    Tank.Update(dto);\n}`],
 TrendChart:['xyz.Core/Client/Common/xyz.Client.Presentation/Models/TrendSeries.cs','创建曲线与通知数据更新',`var temperature = new TrendSeries("Chamber1.Temperature", "℃", false);\n// xs: 升序时间的 ToOADate() 数值，ys: 同样数量的采样值。\ntemperature.SetData(xs, ys);\n// 将 temperature 放进绑定到 Series 的集合；追加数据用 Append。`],
 Robot3D:['xyz.Core/Client/Common/xyz.Client.Presentation/Models/RobotArmModel.cs','每只手臂独立提供状态',`var arms = new ObservableCollection<RobotArmModel>\n{\n    new() { Arm = 1, Extension = 0.8, Heading = 0, Wafer = wafer },\n    new() { Arm = 2, Extension = 0, Heading = 180, Wafer = null }\n};\n// VM 的 Arms 返回这个集合；Rotation 使用 RobotDirection.East 等枚举。`],
 RobotVisual3D:['xyz.Core/Client/Common/xyz.Client.Presentation/Controls/Robot3D.xaml.cs','底层件的普通方法入口（RobotViewBase 派生外观中）',`protected override void RenderFrame(double rotation, double travel)\n{\n    Body.SetPose(ArmType, rotation, travel, ArmVisuals);\n}\n// Body 是视口里的 RobotVisual3D。普通页面优先直接用 Robot3D。`],
 ArmVisual3D:['xyz.Core/Client/Common/xyz.Client.Presentation/Controls/ThreeD/ArmVisual3D.cs','普通集合 Attachments：管路随臂转动，喷液反馈独立',`<hardware:ArmVisual3D\n    Angle="{Binding DataContext.ArmAngle, ElementName=Scene}">\n    <hardware:ArmVisual3D.Attachments>\n        <hardware:FluidPipeVisual3D\n            Length="2.2"\n            IsFlowing="{Binding DataContext.NozzleOn, ElementName=Scene}" />\n    </hardware:ArmVisual3D.Attachments>\n</hardware:ArmVisual3D>`]
};
let html = `<section class="module" id="frontend" data-title="前端">\n<h1>前端</h1>\n<p>这一部分先讲控件：选哪个、怎么放进页面、绑什么数据、每个依赖属性怎么用。${controls.length} 个可实例化控件，其中 ${controls.filter(c=>c.is3D).length} 个提供 3D 网格交互；抽象基类的公共属性列在派生控件表中。</p>\n<section class="chapter" id="fe-controls"><h2>一、控件</h2>\n<section class="topic" id="fe-controls-start"><h3>1.1 先从这里选控件</h3>\n<p>下方原生截图由当前 WPF 控件直接生成；3D 演示使用同一控件导出的真实网格，可以拖动旋转、滚轮缩放、切换示例属性。网页材质与 WPF 的透明、纹理效果可能略有差异，可切到原生截图核对。所有演示均离线，不连接 PLC 或发送设备指令。</p>\n<label class="fe-search-label">查找控件 <input type="search" id="fe-search" placeholder="例如 Sensor、Tank、Bowl、输入、管路" autocomplete="off"></label>\n<div class="fe-index">`;
for (const group of [...new Set(controls.map(groupOf))]) html += `<div><strong>${group}</strong><ul>${controls.filter(c=>groupOf(c)===group).map(c=>`<li data-fe-index="${c.name}"><a href="${typeLink(c)}">${c.name}</a></li>`).join('')}</ul></div>`;
html += `</div><p class="fe-small">平台控件位于 <code>xyz.Core/Client/Common/xyz.Client.Presentation/Controls</code>，35021 自有控件位于项目的 <code>Client/Presentation/Controls</code>。当前独立阀门符号 <code>CdsAssemblyPreview.ValveSymbol</code> 仍是工具原型，不计入平台控件清单。</p></section>\n`;
html += `<section class="topic" id="fe-controls-binding"><h3>1.2 引用、绑定与属性表怎么看</h3>
<p>页面先引用命名空间，平台控件和项目控件分别引用。资源继续沿用应用已经合并的公共资源；单独做预览窗口时，要按 ${ref('xyz.Core/Client/xyz.Client/App.xaml','App.xaml')} 的顺序补齐 MaterialDesign、颜色、字体、控件样式和语言包。</p>
<figure class="code" data-file="xyz.Core/Client/xyz.Client/App.xaml"><figcaption>放在页面根节点上的命名空间声明</figcaption><script type="text/plain">xmlns:controls="clr-namespace:xyz.Client.Presentation.Controls;assembly=xyz.Client.Presentation"
xmlns:hardware="clr-namespace:xyz.Client.Presentation.Controls.ThreeD;assembly=xyz.Client.Presentation"
xmlns:project="clr-namespace:xyz._35021.Client.Presentation.Controls;assembly=xyz.35021.Client.Presentation"</script></figure>
<table><thead><tr><th>表中标记</th><th>使用方式</th></tr></thead><tbody>
<tr><td>默认单向</td><td>元数据没有声明默认双向。显示状态一般用 OneWay；想读回用户交互结果时明确设置 Mode。</td></tr>
<tr><td>默认双向</td><td>元数据声明 BindsTwoWayByDefault；Value、可见时间范围等会回写 VM，VM 需要通知属性变化。</td></tr>
<tr><td>只读</td><td>只能作为绑定来源读取，例如另一控件用 ElementName 读取 MountHeight；不要向只读属性设置值或 Binding。普通 C# 只读属性不一定是只读依赖属性，表中按 DependencyProperty.ReadOnly 标记。</td></tr>
<tr><td>继承自 …</td><td>仍可以在当前控件上绑定；不需要在派生控件重写一份属性。</td></tr>
<tr><td>bool?</td><td>null 是未知，不要用 false 代替。未知、未触发和根本没配点位是不同含义。</td></tr>
<tr><td>Color / Brush</td><td>Color 接颜色；Brush 接画刷。三维件的 BodyColor 不能直接绑定 SolidColorBrush 对象，需要取其 Color。</td></tr>
</tbody></table>
<p><b>三维绑定：</b>独立 Visual3D 不继承页面 DataContext。例子里命名二维宿主 <code>Scene</code>，再用 <code>ElementName=Scene</code> 或显式 Source；<code>Transform</code> 是继承的安装变换。三维几何尺寸用场景示意单位，不是设备毫米值。独立硬件件没有相机和灯光，放入 Viewport3D；以 Scene / View 命名的组合控件通常已自带视口。</p>
<p><b>组合原则：</b>Tank、Sensor、阀门、摆臂、气缸和喷嘴的归属来自实际装机/状态数据，布局只负责位置和连接。<code>Attachments</code> 等普通集合不放进依赖属性表；下面单列它们。例子中的 Binding 路径是调用方 VM 要提供的显示属性，不是写死的 PLC 路径。</p></section>\n`;

for (let i=0;i<controls.length;i++) {
 const c=controls[i], n=note[c.name] || [c.summary,'',{}]; c.example=example(c); c.group=groupOf(c);
 const id='fe-control-'+c.name;
 const dpRows=c.properties.map(p=>`<tr><td><code>${esc(p.name)}</code>${p.owner!==c.name?`<small>继承自 ${esc(p.owner)}</small>`:''}</td><td><code>${esc(p.type)}</code>${p.enumValues.length?`<small>${p.enumValues.map(esc).join(' / ')}</small>`:''}</td><td><code>${esc(p.default)}</code></td><td>${p.readOnly?'只读':p.twoWay?'默认双向':'默认单向'}</td><td>${esc(p.description)}</td></tr>`).join('');
 html+=`<section class="topic fe-control-topic" id="${id}" data-control="${c.name}"><h3>1.${i+3} ${c.name}${c.project?'（35021 项目）':''}</h3>
<p class="fe-kicker">${esc(c.group)} · ${ref(c.source,'查看源码')} · ${c.properties.length} 个公开依赖属性（含自定义基类）</p>
<p>${esc(n[0])}</p><p>${esc(n[1])}</p>
<div class="fe-demo" data-demo="${c.name}">
 <div class="fe-demo-toolbar"><label>示例属性 <select aria-label="${c.name} 示例属性" class="fe-preset">${c.variants.map((v,j)=>`<option value="${j}">${esc(v.label)}</option>`).join('')}</select></label>
 ${c.is3D?'<button type="button" class="fe-reset">恢复视角</button><button type="button" class="fe-native" aria-pressed="false">原生截图</button>':''}
 <span>${c.is3D?'拖动旋转 · 滚轮缩放':'原生 WPF 控件效果'}</span></div>
 <div class="fe-demo-stage"><img class="fe-native-image" src="docs/frontend-controls/${c.variants[0]?.image || ''}" alt="${c.name} 原生 WPF 效果" loading="lazy" width="640" height="360"></div>
 <output class="fe-demo-values">基础示例；实际默认值见下方属性表。</output>
</div>
${c.name==='InputTextBox'?'<div class="fe-input-lab"><label>提交值教学演示（范围 0~100）：<input aria-label="InputTextBox 提交值演示" value="25.5" inputmode="decimal"></label><button type="button">提交</button><output>Value = 25.5</output><small>这是网页输入演示；上方为实际 WPF 控件截图。非法提交保留上次合法 Value。</small></div>':''}
<figure class="code" data-file="${c.source}"><figcaption>最小用法；绑定路径由调用方 ViewModel 提供</figcaption><script type="text/plain">${c.example}</script></figure>
${extra[c.name]?`<figure class="code" data-file="${extra[c.name][0]}"><figcaption>${esc(extra[c.name][1])}</figcaption><script type="text/plain">${extra[c.name][2]}</script></figure>`:''}
${c.properties.length?`<label class="fe-property-filter">筛选依赖属性 <input type="search" placeholder="属性名、类型或用途" aria-label="${c.name} 筛选依赖属性"></label><div class="fe-table-scroll"><table class="fe-properties"><thead><tr><th>属性</th><th>类型 / 枚举</th><th>默认值</th><th>绑定</th><th>怎样使用</th></tr></thead><tbody>${dpRows}</tbody></table></div>`:'<p>没有新增的公开依赖属性。可以使用 Width、Height、Margin、Visibility、DataContext 等继承的 WPF 属性；业务显示方式见上面的说明。</p>'}
${c.members.filter(m=>m.description && ['Attachments','Columns','ArmVisuals','ViewModel'].includes(m.name)).map(m=>`<p><b>普通属性 ${esc(m.name)}</b>（${esc(m.type)}，不是依赖属性）：${esc(m.description)}</p>`).join('')}
</section>\n`;
}
html+='</section>\n</section>';
// Only the generated block and its local asset hooks are replaced. Existing backend chapters remain intact.
const file=path.join(root,'框架讲解.html'); let doc=fs.readFileSync(file,'utf8');
doc=doc.replace(/\r\n/g,'\n');
const marked=`<!-- FRONTEND-CONTROLS:BEGIN -->\n${html.replace(/[ \t]+$/gm,'')}\n<!-- FRONTEND-CONTROLS:END -->\n\n`;
if(doc.includes('<!-- FRONTEND-CONTROLS:BEGIN -->')) doc=doc.replace(/<!-- FRONTEND-CONTROLS:BEGIN -->[\s\S]*?<!-- FRONTEND-CONTROLS:END -->\n*/,marked);
else doc=doc.replace('<section class="module" id="grpc"',marked+'<section class="module" id="grpc"');
if(!doc.includes('frontend-controls/guide.css')) doc=doc.replace('</head>','<link rel="stylesheet" href="docs/frontend-controls/guide.css">\n</head>');
if(!doc.includes('frontend-controls/guide.js')) doc=doc.replace('</body>','<script src="docs/frontend-controls/catalog.js"></script>\n<script src="docs/frontend-controls/guide.js"></script>\n</body>');
doc=doc.replace("var ROOT = 'D:/Common';", "var ROOT = location.protocol === 'file:' ? decodeURIComponent(new URL('.', location.href).pathname).replace(/^\\/(?=[A-Za-z]:\\/)/, '').replace(/\\/$/, '') : 'D:/Code';");
fs.writeFileSync(file,doc.replace(/\n/g,'\r\n'));
fs.writeFileSync(path.join(out,'catalog.js'),'window.FRONTEND_CONTROLS='+JSON.stringify(catalog)+';\n');
console.log(`Updated ${file}: ${controls.length} controls, ${controls.reduce((n,c)=>n+c.properties.length,0)} DP rows`);
