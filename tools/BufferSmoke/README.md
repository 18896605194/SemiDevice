# Buffer 控件

普通页面只需要缓存架时使用 `BufferView`，DataContext 从页面继承，相机和灯光在内部，
没有标题、模块状态、片数文字或按钮：

```xml
xmlns:controls="clr-namespace:xyz.Client.Presentation.Controls;assembly=xyz.Client.Presentation"

<controls:BufferView Width="300" Height="260"
                     SlotCount="10" Wafers="{Binding BufferWafers}" />
```

`BufferView` 的依赖属性为 `FirstSlot`、`SlotCount`、`Wafers` 和只读的 `WaferCount`。
默认最小尺寸 120 × 120；可用继承的 `IsHitTestVisible="False"` 作固定视觉展示。
默认支持拖动旋转、滚轮缩放；`ResetView()` / `ShowBackView()` 可由外部视角按钮调用。
`Standalone.xaml` 为可编译的使用示例。

需要模块卡片时仍使用 `Buffer`，它在同一个 `BufferView` 外添加标题、状态栏和默认 / 背面按钮：

```xml
xmlns:controls="clr-namespace:xyz.Client.Presentation.Controls;assembly=xyz.Client.Presentation"

<controls:Buffer Width="460" Height="420"
                 Title="Buffer" SlotCount="10"
                 Wafers="{Binding BufferWafers}"
                 IsOnline="{Binding IsOnline}"
                 StateText="{Binding StateText}"
                 StateTone="{Binding StateTone}" />
```

使用项目现有暗色主题、字号、文本、边框、按钮和语言资源，与 LoadPort / Chamber 相同。
`Scene.xaml` 是可编译的最小绑定示例；`Wafers` 为 `WaferModel` 集合。
默认最小尺寸为 220 × 280，页面可像普通 WPF 控件一样设置 Width / Height / MinWidth / MinHeight。

| 依赖属性 | 类型 | 默认值 / 用途 |
| --- | --- | --- |
| `Title` | string | `Buffer`，标题 |
| `SlotCount` | int | `10`，槽数，支持 0～100；0 清空场景 |
| `FirstSlot` | int | `1`，底层物理槽号，显示某个槽段时使用 |
| `Wafers` | IEnumerable | null，按 `WaferModel.Slot` 摆片、`State` 上色 |
| `IsOnline` | bool | false，参与调度的在线状态 |
| `StateText` | string | 空，空时隐藏状态徽标 |
| `StateTone` | ModuleStateTone | Inactive，复用模块状态色调 |
| `WaferCount` | int，只读 | 0，当前有效占用槽数 |

已有共享三维场景时使用公开的 `BufferVisual3D`，它直接继承 `HardwareVisual3D`，不创建自己的视口：

```xml
xmlns:hardware="clr-namespace:xyz.Client.Presentation.Controls.ThreeD;assembly=xyz.Client.Presentation"

<!-- 放进已有的 Viewport3D.Children 或 HardwareVisual3D.Attachments；Host 是页面命名宿主。 -->
<hardware:BufferVisual3D
    SlotCount="{Binding DataContext.SlotCount, ElementName=Host}"
    Wafers="{Binding DataContext.BufferWafers, ElementName=Host}" />
```

三维件不继承 DataContext，必须用 `ElementName` / 显式 `Source`。
它提供同样的四个数据依赖属性，并继承 `BodyColor`、`Transform` 等硬件属性；相机和灯光由宿主提供。
`SharedScene.xaml` 是实际编译检查的共享场景示例。
三个入口共用 `BufferVisual3D` 的建模和集合观察逻辑，没有各自维护晶圆列表或创建服务连接。

槽号从底部向上增加。`Slot` 必须是实际物理槽号，不能用来源槽号 `LpSlot` 代替；只显示
`FirstSlot` 到 `FirstSlot + SlotCount - 1` 的片，同槽重复取集合中第一片。
控件不写死 BufferIn / BufferOut，也不把两段账单的局部槽号自动拼接成物理槽号。

支持整体替换集合、`ObservableCollection` 增删 / Reset、单片 `Slot` 移动和 `State` 更新；
数据在 UI 线程更新。普通 List 增删时需要更换集合引用。空槽不画晶圆；十槽满载由调用方提供十片数据。
固定紧凑槽距，前后对称贯通。拖动旋转、滚轮缩放，底部只有默认 / 背面视角按钮；
无俯视、下拉查看、点击选片、选择回传或槽位详情。只展示，不连接服务、不发送设备指令。

运行原生 WPF 检查并导出模块卡片正反面图、独立缓存架视图 `buffer-view.png`：

```powershell
dotnet run --project tools/BufferSmoke -- D:\Code\artifacts\buffer-control.png
```

使用隐藏窗口与模拟数据，无硬件连接。
