# CDS 界面 XAML

ToolBuilder 选择药液模块后，右侧「画布 / XAML」切换查看界面源码；「导出 XAML」输出普通 WPF `UserControl`。「打开 XAML」恢复本工具导出的组件位置、尺寸、旋转、管路和显示参数，继续受当前装机清单约束。源码视图支持选择和复制，参数在右侧编辑。

顶部保存同时生成 `CdsLayouts/<machine文件名>/<药液模块路径>.xml` 和同名 `.xaml`。XML 用于工具继续编辑，XAML 用于项目显示；二者一起进行外部冲突检查、备份和回滚。另存为同时携带两种文件。显式导出不会代替顶部保存或清除未保存标记。

## 项目界面使用

将生成的 XAML 作为项目自己的界面，例如 `Client/Manual/Views/CdsDiagram.xaml`：

1. 在根 `UserControl` 增加项目命名空间的 `x:Class`，例如 `xyz._35021.Client.Manual.Views.CdsDiagram`。
2. 使用项目已有的普通 UserControl 创建方式，配套 `.xaml.cs` 只调用 `InitializeComponent()`；XAML 的生成操作是 `Page`。
3. 在药液手动页面中直接嵌入 `<views:CdsDiagram />`，由父页面继承 DataContext。无需添加 ToolBuilder、编辑模型或几何运行时引用。
4. 使用公共前端控件库及项目已有主题资源（`DarkColors`、`FontSize` 等）。布局缩放由 `Grid` 和 `Viewbox` 适配容器及 WPF DPI，不在后台代码里计算坐标。

这里没有替项目创建新的 CDS 手动页面或后台布局接口。项目也可按照现有页面加载方式读取 XAML，直接承载生成的 UserControl；读取位置由项目现有的配置读取方式决定。

## ViewModel 绑定

导出的实际控件直接绑定 VM 数据。数据结构可以是项目自己的类型，只需要对应属性；工具预览中的记录类型不是项目依赖。组件键是模块内相对路径，例如 `Tank1`、`Tank1.Supply.Valve1`、`Tank1.HH`。管段键是保存的管路 ID。

| VM 属性 | 控件依赖属性 | 数据类型 |
| --- | --- | --- |
| `DiagramStates[组件].FillFraction` | `TankVisual3D.FillFraction` | `double`，0～1 |
| `DiagramStates[组件].IsAlarm` | `TankVisual3D.IsAlarm` | `bool` |
| `DiagramStates[组件].IsOpen` | `ValveSymbol.IsOpen` | `bool?`，null 为未知 |
| `DiagramStates[组件].IsActive` | `SignalLamp.IsActive` | `bool?`，null 为未知 |
| `DiagramStates[组件].State` | `ProcessHardwareScene.State` / `HardwareVisual.State` | 公共 `HardwareState` 枚举 |
| `DiagramStates[组件].Reading / Target` | 同名依赖属性 | `double?` |
| `DiagramStates[组件].PowerFraction` | `ProcessHardwareScene.PowerFraction` | `double`，0～1 |
| `DiagramStates[组件].Unit` | `HardwareVisual.Unit` | `string` |
| `DiagramStates[组件].ReadingText` | 液位/测量值 TextBlock.Text | `string`，含显示单位等 |
| `DiagramPipes[管段].IsFlowing / IsFilled` | `FluidRouteVisual3D` 同名依赖属性 | `bool` |

VM 收到现有接口数据后，更新这些属性并发出 PropertyChanged。替换字典时通知 `DiagramStates` / `DiagramPipes`；保留字典时其值对象应通知对应属性。可以直接把生成的绑定改为项目现有的 VM 属性名称。

例如实际生成的阀门是：

```xml
<fluid:ValveSymbol
    IsOpen="{Binding DataContext.DiagramStates[Tank1.Supply.Valve1].IsOpen, ElementName=CdsRoot, Mode=OneWay}" />
```

`fluid` 指向 `clr-namespace:xyz.Client.Presentation.Controls.Fluid;assembly=xyz.Client.Presentation`。泵和加热器在 CDS 使用 `ProcessHardwareScene ViewMode="Front2D"`；它们只在装机配置包含相应组件时生成。

`CdsDiagram.Id / Component / From / To` 等附加属性仅标识元件和接线，方便工具重新打开；不执行渲染、读取设备状态或控制操作。显示控件不发送 PLC/RPC 命令。管路连通和运行条件由项目 VM 提供，导出界面不推断真实工艺流程。
