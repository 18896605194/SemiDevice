# 三维管路装配与底层验证

普通页面请使用 [FluidPipe 普通控件](../FluidPipeSmoke/README.md)：

```xml
<controls:FluidPipe Width="200" IsFlowing="{Binding IsSupplying}" />
```

它可直接放入 Grid 或 Canvas，并正常继承 DataContext。下面的三维装配示例只用于已有的共享三维场景或内部渲染验证。
`FluidRouteVisual3D` 已加入 `xyz.Client.Presentation` 项目，命名空间为 `xyz.Client.Presentation.Controls.ThreeD`。
它继承现有的 `HardwareVisual3D`，可以直接放入 `Viewport3D.Children` 或 `Arm.Attachments`。
腔体、Tank、CDS 共用此控件，只需提供不同的路径和状态。控件只负责显示，不依赖服务端，也不发送设备指令。

## 三维装配示例

下面是一个完整的场景片段。应用需要像现有页面一样合并 `Styles/DarkColors.xaml`。
示例设为正在流动，复制后即可看到效果；已有三维场景只需加入里面的 `FluidRouteVisual3D`，继续使用原来的相机和灯光。

```xml
<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:threeD="clr-namespace:xyz.Client.Presentation.Controls.ThreeD;assembly=xyz.Client.Presentation"
      x:Name="PipeHost"
      Background="{DynamicResource DarkControlBackground}">
    <Viewport3D>
        <Viewport3D.Camera>
            <OrthographicCamera Position="-3,3.7,9"
                                LookDirection="3,-2.6,-9"
                                UpDirection="0,1,0"
                                Width="6.4" />
        </Viewport3D.Camera>
        <ModelVisual3D>
            <ModelVisual3D.Content>
                <Model3DGroup>
                    <AmbientLight Color="#808080" />
                    <DirectionalLight Color="#EEEEEE" Direction="1,-2,-3" />
                </Model3DGroup>
            </ModelVisual3D.Content>
        </ModelVisual3D>
        <threeD:FluidRouteVisual3D
            x:Name="SupplyPipe"
            Path="-2.5,1.9,0 -0.5,1.9,0 -0.5,0.45,0 2.5,0.45,0"
            PipeRadius="0.10"
            WallThickness="0.022"
            FluidCoreRatio="0.58"
            BendRadius="0.34"
            WallOpacity="0.18"
            FlowColor="#52D4EF"
            IsFilled="True"
            IsFlowing="True"
            FlowSpeed="0.55" />
    </Viewport3D>
</Grid>
```

坐标使用场景统一单位，Y 轴向上，默认从第一个点向最后一个点流动。
每个点表示管中心的位置，弯头自动做圆角；多分支使用多个实例，以共享端点连接。
整体位置、旋转和缩放使用继承的 `Transform`。

## 绑定实际反馈和路径

外部可配置的参数均为依赖属性，可直接写值或绑定。三维对象不提供自己的 `DataContext` 属性，建议与项目其他硬件一样显式引用宿主：

```xml
<threeD:FluidRouteVisual3D
    Path="{Binding DataContext.SupplyRoute, ElementName=PipeHost, Mode=OneWay}"
    PipeRadius="0.10"
    WallThickness="0.022"
    BendRadius="0.34"
    FlowColor="{Binding DataContext.SupplyFlowColor, ElementName=PipeHost, Mode=OneWay}"
    IsFlowing="{Binding DataContext.SupplyIsFlowing, ElementName=PipeHost, Mode=OneWay}" />
```

`SupplyRoute` 是调用方提供的 `Point3DCollection`，
`SupplyFlowColor` 为 `System.Windows.Media.Color`，
`SupplyIsFlowing` 为布尔反馈。名称是示例，接入时换成实际 ViewModel 属性。
ViewModel 按项目规范继承 `BaseViewModel`，通过 `SetProperty` 通知整条路径替换。
逐个修改集合中的点时，控件会监听集合变化，不需要手动刷新。

未使用绑定时，也可以直接在代码中设置：

```csharp
SupplyPipe.Path = new Point3DCollection
{
    new Point3D(-2.5, 1.9, 0),
    new Point3D(-0.5, 1.9, 0),
    new Point3D(-0.5, 0.45, 0),
    new Point3D(2.5, 0.45, 0)
};

SupplyPipe.IsFlowing = true;
SupplyPipe.Path[1] = new Point3D(-0.5, 2.2, 0);
```

已有绑定时应修改绑定源。像其他 WPF 依赖属性一样，直接给 CLR 属性赋值会替换该属性的绑定。
路径集合的编辑在 UI 线程执行；冻结的集合可以绑定，但需要先克隆再编辑。

## 属性

| 依赖属性 | 默认值 | 作用 |
| --- | --- | --- |
| `Path` | 每个实例独立的空集合 | 管中心三维路径；支持整体替换及集合内单点修改 |
| `PipeRadius` | 0.06 | 管外半径，必须大于 0 |
| `WallThickness` | 0.012 | 壁厚，必须大于 0；实际厚度最多取外半径的 90% |
| `FluidCoreRatio` | 0.58 | 液芯半径占内腔半径的比例，范围 (0, 1]；用于留出透明间隙，不是充液率 |
| `BendRadius` | 0.22 | 弯头圆角半径，短段自动缩小；0 为折线 |
| `WallOpacity` | 0.18 | 管壁不透明度强度，范围 [0, 1]；内外壁按比例使用，0 时不显示管壁 |
| `WallColor` | #B4B4B4 | 中性管壁颜色 |
| `FluidColor` | #C4C4C4 | 液体底色，与 FlowColor 按 1:3 混合得到液芯颜色 |
| `FlowColor` | #52D4EF | 水流主色和移动水纹颜色；#F2F2F2 可用于清水折光 |
| `IsFilled` | true | 是否有液体，false 时不显示液芯 |
| `IsFlowing` | false | 是否正在流动；停流清除液芯和水纹，只留下透明管壁 |
| `FlowSpeed` | 0.55 | 动画速度，场景长度/秒；0 暂停，正值显示速度限制在 0.001～1000；不是实测流量 |
| `ReverseFlow` | false | 反向流动 |
| `IsAnimationEnabled` | true | false 时冻结动画并保留当前液芯 |

`IsFlowing=false` 不会修改 `IsFilled`；重新流动时按这两个属性重新显示。
`IsAnimationEnabled=false` 和 `FlowSpeed=0` 只暂停水纹，与停流不同。
控件内部不覆盖外部绑定，也不把显示状态反写到设备。

## 组合与运行

可以把 `FluidRouteVisual3D` 放进 `ArmVisual3D.Attachments`，路径使用 Arm 的局部坐标，随 Arm 一起运动。
多个实例的路径、颜色和流动状态互相独立。页面隐藏或管路移除时停止动画，重新显示时恢复。
整套硬件动态挂载或拆卸应通过 `HardwareVisual3D` 容器，例如 Arm 或 ChamberBase，使后代能收到连接变化通知。

动画只改变纹理偏移，使用 30 帧/秒的 WPF 动画时钟，不逐帧重建网格。
几何计算放在 `FluidRouteGeometry.cs`，依赖属性、材质和动画放在 `FluidRouteVisual3D.cs`。
方法、条件、循环与属性访问器均采用大括号单独一行的写法。

现有 `ChamberScene` 的 `FluidPipeVisual3D` 喷嘴已在内部复用本控件，透明供液管和外部水纹与普通 `FluidPipe` 控件一致。
喷口外的自由液柱和落点水花属于场景效果。管路之间的空间避让由调用方的路径布局决定。

## 验证

从仓库根目录执行：

```powershell
dotnet run --project tools/FluidRouteVisual3DSmoke -- artifacts/fluid-route-verification/fluid-route-wpf.png
```

`Scene.xaml` 是实际参与编译的绑定示例。
测试使用模拟反馈，不连接设备；覆盖真实 WPF 渲染、流动/停流/暂停、颜色和尺寸绑定、
路径替换与点位编辑、冻结集合、多实例隔离、Arm 挂载以及隐藏/移除时的动画生命周期。
成功后输出原生 WPF 渲染图。
