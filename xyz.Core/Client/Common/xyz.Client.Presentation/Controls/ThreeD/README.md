# 三维硬件组件

`ArmVisual3D`、`LiftVisual3D`、`FluidPipeVisual3D`、`BowlVisual3D` 和 `HomeCupVisual3D` 是平台的摆臂、升降气缸、管子、Bowl 和 Home 排液杯组件，共用页面的 `Viewport3D`。
多个硬件共用相机、灯光和三维空间，后续可与主轴等组件组装。
组件内部不创建独立视口、不连接服务、不发送设备指令。

## ChamberBase 底座与整体装配

`ChamberBaseVisual3D` 是深灰色切角底板，默认 BodyColor 为 `#333333`。
Length（X 方向，默认 `5.6`）、Width（Z 方向，默认 `4.4`）、Thickness（默认 `0.12`）
均为可绑定的依赖属性，只接受有限正数。尺寸采用场景统一的示意单位。

底座上表面中心是局部原点，Y 向上，底板向下延伸至 `-Thickness`。
调整板厚不会移动安装面；调整长宽也不会自动缩放、平移内部硬件，装配层应设置合适的尺寸和位置。
固定结构不增加升降或设备控制逻辑；继承的选择、颜色属性可用，IsMoving 不必绑定。

```xml
<hardware:ChamberBaseVisual3D Length="5.6" Width="4.4" Thickness="0.12">
    <hardware:ChamberBaseVisual3D.Attachments>
        <hardware:BowlVisual3D HeightLevel="1">
            <hardware:BowlVisual3D.Transform>
                <TranslateTransform3D OffsetX="-0.5" />
            </hardware:BowlVisual3D.Transform>
        </hardware:BowlVisual3D>
        <hardware:HomeCupVisual3D>
            <hardware:HomeCupVisual3D.Transform>
                <TranslateTransform3D OffsetX="1.55" OffsetZ="1.1" />
            </hardware:HomeCupVisual3D.Transform>
        </hardware:HomeCupVisual3D>
    </hardware:ChamberBaseVisual3D.Attachments>
</hardware:ChamberBaseVisual3D>
```

Attachments 是底座的子 Visual3D 集合；每个硬件使用自己的 Transform 设置安装位置。
底座外部 Transform 的旋转、平移或缩放作用于整套结构，包括 Arm.Attachments 内的管子。
硬件动作仍各自绑定：Arm 摆动不移动 HomeCup，Lift 只通过装配绑定带动 Arm，Bowl 升降不改变底座。
底座选中也不会修改子组件的选中或动作状态。
从视口移除底座会通知其后代硬件刷新宿主，管子流动时钟随之停止，重新挂载后按原有反馈恢复。
完整装配示例见 `tools/ChamberBaseVisual3DSmoke/Scene.xaml`。

## Arm 外部绑定

| 依赖属性 | 含义 | 默认值 |
| --- | --- | --- |
| `Angle` | 当前角度反馈，单位为度 | `0` |
| `IsMoving` | 动作反馈；为 true 时自动高亮 | `false` |
| `IsSelected` | 外部选中状态，与动作反馈独立 | `false` |
| `Length` | 回转中心至末端安装块中心的距离 | `2.2` |
| `Width` / `Thickness` | 臂身宽度 / 厚度 | `0.23` / `0.085` |
| `PivotRadius` | 回转座半径 | `0.285` |
| `BodyColor` | 默认金属色 | `#686868` |
| `HighlightColor` | 动作和选中的强调色 | `#42A5F5` |
| `HighlightStrength` | 强调色混入比例，范围 0～1 | `0.42` |
| `Transform`（继承） | 场景安装位置、整体升降等外部变换 | 恒等变换 |

默认尺寸是示意比例。调用方统一场景长度单位，并按照实际设备设置尺寸；尺寸须为有限正数，角度须为有限数。
局部坐标 Y 向上，原点为回转中心，0° 沿 +X，+90° 指向 -Z。
`Angle` 仅改变内部旋转变换，不重建网格，不覆盖外部 `Transform`。

下例假定宿主 `DataContext` 已由应用提供，具有 `ArmAngle`、`ArmIsMoving` 属性；
应用已合并 `Styles/DarkColors.xaml`。三维对象不是 `FrameworkElement`，这里显式通过命名宿主绑定，
不依赖三维对象自身的 DataContext。代码中也可以用 `BindingOperations.SetBinding` 配合显式 `Source`。

```xml
<Grid x:Name="ChamberScene"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:hardware="clr-namespace:xyz.Client.Presentation.Controls.ThreeD;assembly=xyz.Client.Presentation"
      Background="{StaticResource DarkSurfaceBackground}">
    <Viewport3D>
        <Viewport3D.Camera>
            <OrthographicCamera Position="3.1,2.6,4.8"
                                LookDirection="-2,-2.6,-4.8"
                                UpDirection="0,1,0" Width="3.5" />
        </Viewport3D.Camera>
        <ModelVisual3D>
            <ModelVisual3D.Content>
                <Model3DGroup>
                    <AmbientLight Color="#707070" />
                    <DirectionalLight Color="#C0C0C0" Direction="-1,-3,-2" />
                </Model3DGroup>
            </ModelVisual3D.Content>
        </ModelVisual3D>
        <hardware:ArmVisual3D
            Angle="{Binding DataContext.ArmAngle, ElementName=ChamberScene, Mode=OneWay}"
            IsMoving="{Binding DataContext.ArmIsMoving, ElementName=ChamberScene, Mode=OneWay}"
            BodyColor="{StaticResource DarkHardwareBodyColor}"
            HighlightColor="{Binding Color, Source={StaticResource DarkAccent}, Mode=OneWay}" />
    </Viewport3D>
</Grid>
```

## Lift 外部绑定

气缸只需要一个布尔输入 `IsRaised`：`true` 升起，`false` 收回，默认 `false`。
行程、连续位置和动画时间均为组件内部的示意参数，不向调用方开放。
外观尺寸可以通过场景的 `Transform` 缩放。

```xml
<hardware:LiftVisual3D x:Name="Lift"
    IsRaised="{Binding DataContext.LiftIsRaised, ElementName=ChamberScene, Mode=OneWay}"
    BodyColor="{StaticResource DarkHardwareBodyColor}"
    HighlightColor="{Binding Color, Source={StaticResource DarkAccent}, Mode=OneWay}" />
```

`LiftIsRaised` 由调用方提供；不要把设备 Open/Close、上下位反馈的业务映射放进外观组件。
组件在场景内收到状态切换后，用约 450 ms 完成全行程视觉过渡；中途反向会从当前显示位置继续。
动画期间自动高亮，无需另外设置 `IsMoving`；到位后取消动画高亮。
这段动画是状态变化的视觉提示，不代表气缸的实际运动时间或连续位置测量。
装入场景前赋值时直接显示对应初始状态。动画完成后清除时钟，不保留逐帧事件或后台计时器。

Lift 同样继承 `IsSelected`、`BodyColor`、`HighlightColor`、`HighlightStrength` 和 `Transform`。
如果以后还需要展示真实的“动作尚未结束”反馈，可选地绑定继承的 `IsMoving`；
内部动画不会修改或覆盖这个绑定。

`MountHeight` 是只读依赖属性，表示顶端安装面当前的局部 Y 坐标，随动画更新。
装配层可以将它绑定到 Arm 外部平移变换的 `OffsetY`，并叠加 Arm 安装面的局部偏移。
Lift 的原点在底座底面中心，沿 +Y 伸出；整个装配的安装位置/缩放应放到共同父节点上。
`MountHeight` 不含外部 `Transform`，所以不用重复叠加场景安装位置。
Arm 的角度和动作状态仍独立提供，Arm 回 Home 不会改变 `IsRaised`。

## Bowl 外部绑定

组件名为 `BowlVisual3D`，界面称为 Bowl。它是敞开的薄壁圆环，不包含晶圆、主轴、检测器或侧面气缸。

| 依赖属性 | 含义 | 默认值 |
| --- | --- | --- |
| `HeightLevel` | 外观高度等级，只接受整数 1、2、3 | `1` |
| `IsRaised` | 整体升降：true 上位、false 下位 | `false` |
| `Radius` | 外半径，用于不同直径的 Bowl 装配 | `1.59` |

1 级沿用已确认的低矮薄壁比例，高度为 `0.275`；2 级为 `0.55`，3 级为 `0.825`。
三级比例暂按 1:2:3 设置，是场景示意单位，不代表毫米。高度等级只延长侧壁，
保持相同半径、薄壁截面和底部安装面。改变 Radius 时内径按相同比例调整，不改变高度等级。

```xml
<hardware:BowlVisual3D
    HeightLevel="{Binding DataContext.BowlHeightLevel, ElementName=ChamberScene, Mode=OneWay}"
    IsRaised="{Binding DataContext.BowlIsRaised, ElementName=ChamberScene, Mode=OneWay}"
    BodyColor="{StaticResource DarkHardwareBodyColor}"
    HighlightColor="{Binding Color, Source={StaticResource DarkAccent}, Mode=OneWay}" />
```

HeightLevel 是外观规格，不是升降挡位。IsRaised 变化时整体沿 Y 轴过渡并自动高亮，
结束后恢复默认材质；外部 IsMoving 或 IsSelected 仍为 true 时继续高亮。
内部行程和动画时长采用示意值，不作为设备指令或实际位置反馈。
默认原点位于下位底面中心，外部 Transform 用于安装位置，升降不会覆盖它。
多个 Bowl 分别绑定各自状态，不互相联动。页面隐藏或从视口移除后停止动画并直接呈现目标状态。
通过硬件节点动态挂载；普通 ModelVisual3D 包装节点的拆卸不会通知内部硬件，有限过渡会自行结束。

## Disk：基于二维 Wafer 的三维盘

`DiskVisual3D` 使用自己的 Wafer 实例作为可交互三维盘面，复用二维的状态色、右键菜单及悬停逻辑。
原有 `Wafer.xaml`、`Wafer.xaml.cs` 和调度界面保持不变。实例之间只通过调用方绑定共享业务数据，视觉状态独立。
三维实体补充厚度、边缘高亮与方向标记；颜色高亮位于侧壁/外圈，保留盘面的业务状态色。

| 二维接口 | 三维对应行为 |
| --- | --- |
| `Data` | 接收同一片数据对象，动态跟踪 Data.State、Data.LpSlot |
| `CreateCommand` / `DeleteCommand` | 沿用右键建片/删片菜单，调用外部命令 |
| `CommandParameter` | 原样传给外部命令 |
| `CreateEnable` / `DeleteEnable` | 控制菜单使能，同时尊重命令 CanExecute |
| `IsDiskVisible` | 无片数据时仍显示圆盘；默认 false |
| `FillColor` | 无匹配业务状态时的盘面 Brush；默认 Gray |
| `BorderBrush` / `BorderThickness` | 盘面边线 Brush 和 Thickness，沿用二维逻辑单位 |
| `RotationSpeed` | 度/秒，大于 1 开始旋转，0～1 停止；不直接绑定 RPM |
| `RotateClockwise` | 从上方看顺时针 / 逆时针；默认 true |
| `Label` | 三维支持非空 Label 覆盖文字，否则显示 Data.LpSlot |

二维 Wafer 已声明 Label，但当前圆形模板仍固定使用 Data.LpSlot；三维仅在私有实例上补齐覆盖行为，
不会改变二维控件。业务状态色完整沿用 IdleNojob、IdleHasjob、Process、Completed、Error、
RcmCompleted、Soaking、Transfer、Crossed、Double 十种状态。

额外的 `Radius`（默认 1.32）和 `Thickness`（默认 0.045）控制三维外观，必须为有限正数。
原点为盘底中心，Y 向上；用 Transform 把盘放进 Bowl。停止、换速和换向保留当前角度。
页面隐藏、无盘显示或移除所属底座后停止动画，重新显示时按输入状态继续。
IsMoving / IsSelected 保留公共硬件高亮约定，不向设备发指令。

```xml
<!-- 放进 ChamberBaseVisual3D.Attachments，命名宿主为 ChamberScene。 -->
<hardware:DiskVisual3D
    Data="{Binding DataContext.Wafer, ElementName=ChamberScene}"
    RotationSpeed="{Binding DataContext.SpinDisplaySpeed, ElementName=ChamberScene}"
    RotateClockwise="{Binding DataContext.SpinClockwise, ElementName=ChamberScene}"
    CreateCommand="{Binding DataContext.CreateWaferCommand, ElementName=ChamberScene}"
    DeleteCommand="{Binding DataContext.DeleteWaferCommand, ElementName=ChamberScene}"
    CommandParameter="{Binding DataContext.Slot, ElementName=ChamberScene}"
    CreateEnable="{Binding DataContext.CanCreateWafer, ElementName=ChamberScene}"
    DeleteEnable="{Binding DataContext.CanDeleteWafer, ElementName=ChamberScene}"
    IsDiskVisible="True" Radius="1.32" Thickness="0.045">
    <hardware:DiskVisual3D.Transform>
        <TranslateTransform3D OffsetX="-0.5" OffsetY="0.12" />
    </hardware:DiskVisual3D.Transform>
</hardware:DiskVisual3D>
```

宿主 Viewport3D 必须允许命中测试（不要设置 IsHitTestVisible=false），才能操作三维盘面右键菜单。
交互位于盘的上表面，侧壁只显示三维外观。应用继续提供 common.create / common.delete 本地化资源。
无 Data、无 IsDiskVisible 且无 CreateCommand 时隐藏；有 CreateCommand 时保留透明的空片建片区域。
整个装配直接挂在 Viewport3D 或硬件父节点下，遵守前述生命周期约定。
实现采用 WPF [Viewport2DVisual3D 可交互三维表面](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.media3d.viewport2dvisual3d)。

## Door 腔体升降门

`DoorVisual3D` 表示气缸驱动的上下升降门，外观仅显示固定门框和门板，不显示缸体、伸缩杆和外侧连接件。
外部操作状态只绑定 `IsOpen`：true 打开，false 关闭。
门板上升打开、下降关闭。打开后门板底边高于门框最高点，整个门洞完全空出；尺寸和安装位置仍为示意。
Width 默认 `2.2`、Height 默认 `0.58`，控制门板外观尺寸，均为有限正数。
行程和连续位置不向外开放。状态变化时自动播放约 450 ms 的全行程过渡并高亮，反向从当前位置继续。

```xml
<!-- 放在 ChamberBaseVisual3D.Attachments 内，底座直接挂在 Viewport3D 中。 -->
<hardware:DoorVisual3D
    IsOpen="{Binding DataContext.DoorIsOpen, ElementName=ChamberScene, Mode=OneWay}"
    BodyColor="{StaticResource DarkHardwareBodyColor}"
    HighlightColor="{Binding Color, Source={StaticResource DarkAccent}, Mode=OneWay}">
    <hardware:DoorVisual3D.Transform>
        <TranslateTransform3D OffsetX="-0.5" OffsetZ="2.3" />
    </hardware:DoorVisual3D.Transform>
</hardware:DoorVisual3D>
```

装配示意位于底座前侧、Bowl 外侧；位置和方向由外部 Transform 调整。
局部原点为门框下方安装面中心，门宽沿 X、升降沿 Y。
需给上方升起的门板留空间；门板沿框前侧滑动，与横梁保留间隙。示意位置不代表实际设备安装坐标。
门动作不改变 Lift、Arm、Bowl 或供液状态；气缸反馈到 IsOpen 的业务映射由外部负责。
动画完成、页面隐藏或所属底座移除后清除时钟；隐藏时直接呈现当前目标状态。
继承的 IsSelected、IsMoving 可独立绑定，内部过渡不会覆盖外部反馈。
不要将完整装配包在动态挂载的普通 ModelVisual3D 中；挂载/移除应通过硬件节点传递生命周期通知。

## HomeCup 外部绑定

`HomeCupVisual3D` 是固定在 Home 喷嘴下方的接液杯，包含敞口、向下收拢的内壁和贯通的底部排液口。
外观沿用已确认的暗灰色设计，尺寸为示意单位。

| 依赖属性 | 含义 | 默认值 |
| --- | --- | --- |
| `Radius` | 杯口外半径 | `0.32` |
| `Height` | 排液口底面到杯口的总高度 | `0.655` |
| `IsDraining` | 接液/排液状态，为 true 时高亮 | `false` |

```xml
<hardware:HomeCupVisual3D
    Radius="0.32" Height="0.655"
    IsDraining="{Binding DataContext.HomeCupIsDraining, ElementName=ChamberScene, Mode=OneWay}"
    BodyColor="{StaticResource DarkHardwareBodyColor}"
    HighlightColor="{Binding Color, Source={StaticResource DarkAccent}, Mode=OneWay}">
    <hardware:HomeCupVisual3D.Transform>
        <!-- 示意安装位置；由装配层按 Arm 的 Home 喷嘴位置确定。 -->
        <TranslateTransform3D OffsetX="2.2" />
    </hardware:HomeCupVisual3D.Transform>
</hardware:HomeCupVisual3D>
```

原点在排液口底面中心，Y 向上，杯口中心为局部 `(0, Height, 0)`。
Radius 和 Height 必须为有限正数，改变尺寸时保持截面比例。
HomeCup 应与 Arm 并列放在场景中，不放入 Arm.Attachments；Arm 摆动或 Lift 升降时，接液杯保持固定。
喷液继续使用 FluidPipeVisual3D。装配层设置管子的 StreamLength，使落点位于杯内。
可由外部根据实际 Home 到位、供液反馈等确定 HomeCupIsDraining；组件不会自行判断 Arm 位置，
不会控制 Lift、开启供液或调用排液阀。IsDraining 是显示状态，不代表检测到真实液流。
它只切换材质，不新增动画时钟或重复显示液柱。IsSelected 和 IsMoving 沿用公共组件规则。

## 管子与 Arm 装配

`FluidPipeVisual3D` 就是一根管子，DIW、SC1 分别创建实例，互不影响。
功能输入是布尔依赖属性 `IsFlowing`：`true` 高亮管子并显示移动亮段、下落液滴和落点波纹；
`false` 隐藏液体并恢复管子默认材质（仍选中时保留选中高亮）。
可选的 `IsAnimationEnabled=false` 暂停视觉动画，保留出液状态；默认 `true`。

把管子放进 `Arm.Attachments`，它就会继承 Arm 的内部角度和外部安装、升降变换，
不需要重复绑定角度。普通 `Arm.Children` 不包含内部角度变换，应使用专用挂载集合。
管子自己的 `Transform` 用于设置相对摆臂的安装偏移。

```xml
<hardware:ArmVisual3D x:Name="Arm"
    Angle="{Binding DataContext.ArmAngle, ElementName=ChamberScene, Mode=OneWay}">
    <hardware:ArmVisual3D.Attachments>
        <hardware:FluidPipeVisual3D
            IsFlowing="{Binding DataContext.DiwIsFlowing, ElementName=ChamberScene, Mode=OneWay}"
            Length="{Binding Length, ElementName=Arm, Mode=OneWay}">
            <hardware:FluidPipeVisual3D.Transform>
                <TranslateTransform3D OffsetY="0.1" OffsetZ="-0.075" />
            </hardware:FluidPipeVisual3D.Transform>
        </hardware:FluidPipeVisual3D>
        <hardware:FluidPipeVisual3D
            IsFlowing="{Binding DataContext.Sc1IsFlowing, ElementName=ChamberScene, Mode=OneWay}"
            Length="{Binding Length, ElementName=Arm, Mode=OneWay}">
            <hardware:FluidPipeVisual3D.Transform>
                <TranslateTransform3D OffsetY="0.1" OffsetZ="0.075" />
            </hardware:FluidPipeVisual3D.Transform>
        </hardware:FluidPipeVisual3D>
    </hardware:ArmVisual3D.Attachments>
</hardware:ArmVisual3D>
```

`Length` 默认 `2.2`，是沿局部 +X 的直管长度，通常绑定 Arm.Length。
末端喷口位于 `(Length, -0.21, 0)`，沿 -Y 出液。
`StreamLength` 默认 `0.55`，是喷口到接液面的示意距离，设置为 `0` 时隐藏外部液柱和波纹。
这些是装配尺寸，不是流量或设备动作指令。完整腔体装配需要根据实际升降高度、晶圆或 Home 排液杯位置
设置或绑定接液距离，管子本身不推断 Home、Lift 状态，不自动改变出液开关。
共享的颜色、选择和外部动作属性与其他硬件相同，可绑定 DarkHardwareBodyColor 和 DarkAccent。

页面隐藏时暂停动画，重新显示后按当前反馈恢复；从视口移除 Arm 或从 Attachments 移除管子也会停止时钟。
场景应通过硬件节点执行挂载、移除；若另行使用普通 ModelVisual3D 包装整个装配并动态拆卸，
装配层需同时关闭 IsAnimationEnabled。动画只改变变换，复用冻结网格，不注册全局逐帧事件。

## 动作与高亮约定

- `IsMoving=true`、`IsSelected=true` 或组件内部过渡动画进行中时使用相同蓝色材质；三者都结束时恢复默认材质。
- 未选中的组件收到动作反馈也会高亮。停止时若仍被选中，保留选中高亮。
- 角度刷新不会自行把 `IsMoving` 改成 true；`IsMoving=true` 也不会自行启动摆动。
- 设备停止、急停或故障后的状态由外部反馈更新。动画预览可由外部改变 `Angle` 和 `IsMoving`，无需接入硬件。
- Home 是外部给定的角度/位置，不与 Lift 升降、喷液或排液绑定。Arm 不包含气缸、喷嘴和供液管路。
- `Content` 由组件管理；页面通过公开依赖属性和 `Transform` 组合，不直接替换内部模型。

公共的 `HardwareVisual3D` 负责动作/选中高亮，`SetVisualActive` 提供独立的内部动作高亮入口。
后续硬件组件继承它并用 `AddPart` 注册网格，
即可获得同一套材质行为。机械连接通过挂载集合或装配层位置绑定实现，各组件的设备状态仍独立。
静态网格、材质冻结；动作只更新变换/材质，不注册全局逐帧事件。

## 验证

仓库根目录执行 `dotnet run --project tools/ArmVisual3DSmoke`。
也可传入输出 PNG 的绝对路径，生成真实 WPF 的静止/动作对比图：

```powershell
dotnet run --project tools/ArmVisual3DSmoke -- D:\Code\artifacts\arm-wpf-states.png
```

测试使用模拟反馈，不连接设备。覆盖 XAML 绑定、反馈源切换、转轴方向、独立安装高度、
动作与选择状态组合、主题更新、多实例隔离、尺寸更新、网格复用、面朝向和非法值。
渲染模式还检查三维区域有实际像素变化。

Lift 检查与真实 WPF 三态预览：

```powershell
dotnet run --project tools/LiftVisual3DSmoke -- D:\Code\artifacts\lift-wpf-states.png
```

覆盖单布尔绑定、初始上位、上升/下降、中途反向、到位去除高亮和动画时钟、外部动作绑定保留、
选择状态、多组件独立性、安装高度绑定、缸体固定和网格复用。PNG 依次为下位、上升中、上位。

Bowl 三级高度、实际升降、动作高亮、薄壁网格和生命周期检查，附带三级对比图：

```powershell
dotnet run --project tools/BowlVisual3DSmoke -- D:\Code\artifacts\bowl-wpf-levels.png
```

HomeCup 绑定、尺寸、贯通排液口与实际高亮渲染检查：

```powershell
dotnet run --project tools/HomeCupVisual3DSmoke -- D:\Code\artifacts\homecup-wpf-states.png
```

底座尺寸绑定、整体变换、嵌套管路及拆卸生命周期检查，附带独立底座和装配预览：

```powershell
dotnet run --project tools/ChamberBaseVisual3DSmoke -- D:\Code\artifacts\chamber-base-wpf.png
```

Door 开关、反向、自动高亮、固定门框和整套拆卸检查，输出关闭/动作中/打开的总装图：

```powershell
dotnet run --project tools/DoorVisual3DSmoke -- D:\Code\artifacts\door-wpf-states.png
```

Disk 二维/三维功能对照、十种状态色、菜单命令、旋转及生命周期检查：

```powershell
dotnet run --project tools/DiskVisual3DSmoke -- D:\Code\artifacts\disk-wpf-parity.png
```

WPF 机制参考：[自定义依赖属性](https://learn.microsoft.com/zh-cn/dotnet/desktop/wpf/properties/custom-dependency-properties)、
[三维性能建议](https://learn.microsoft.com/zh-cn/dotnet/desktop/wpf/graphics-multimedia/maximize-wpf-3d-performance)。

