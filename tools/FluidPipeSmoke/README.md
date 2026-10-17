# FluidPipe 普通管路控件

`FluidPipe` 位于 `xyz.Client.Presentation.Controls`，是普通 WPF `UserControl`。
像 TextBox 一样直接放进 Grid、Canvas 或 StackPanel。相机、灯光和自动取景由控件内部完成，外部只设置属性。

页面根节点声明一次命名空间：

```xml
xmlns:controls="clr-namespace:xyz.Client.Presentation.Controls;assembly=xyz.Client.Presentation"
```

## 最简单的用法

```xml
<controls:FluidPipe Width="200" IsFlowing="{Binding IsSupplying}" />
```

不写路径时就是直管。默认管外直径为 14；默认期望尺寸为 200 × 24，仍按 WPF 的 Width、Height、Margin 和对齐方式参与布局。
`IsSupplying` 是示例反馈属性，替换成实际 ViewModel 属性即可。DataContext 正常从页面继承。

`IsFlowing=true` 显示彩色水流并播放水纹；`false` 时液芯和水纹消失，只留下透明管壁。
想直接看效果时可先写 `IsFlowing="True"`。

竖管只需要指定方向和高度：

```xml
<controls:FluidPipe Orientation="Vertical"
                    Height="200"
                    IsFlowing="{Binding IsSupplying}" />
```

## 自己画弯管

```xml
<controls:FluidPipe
    Points="0,0 180,0 180,120 340,120"
    PipeThickness="14"
    BendRadius="24"
    FlowColor="#52D4EF"
    IsFlowing="{Binding IsSupplying}" />
```

每一组是 `X,Y`，X 向右，Y 向下。上例依次向右、向下、再向右，拐角自动变圆。
默认由点的范围、管径和边缘空隙决定控件尺寸，管中心路径会平移到控件内部。
设置 Width / Height 后整条自定义路径等比适配，包括管径，不拉坏弯头。
默认直管则随 Width / Height 改变长度，管径保持不变；空间不足时整体缩小。

Points 为空或 null 时显示默认直管；只有一个点或存在非有限坐标时不显示管路。

动态布管时绑定 `PointCollection`：

```xml
<controls:FluidPipe Points="{Binding SupplyPoints}"
                    IsFlowing="{Binding IsSupplying}"
                    FlowColor="{Binding WaterColor}" />
```

`SupplyPoints` 类型是 `System.Windows.Media.PointCollection`，
`WaterColor` 类型是 `System.Windows.Media.Color`。
修改集合中的点会自动刷新形状和布局；更换集合时由 ViewModel 按项目规范通过 `SetProperty` 发出通知。
点集合在 UI 线程修改；冻结集合可绑定，编辑前需要克隆。

## 常用属性

以下全部是依赖属性，支持普通绑定和 WPF Style Setter。

| 属性 | 默认值 | 用途 |
| --- | --- | --- |
| `Points` | 独立空集合 | 二维路径点；留空自动生成直管 |
| `Orientation` | Horizontal | 默认直管的方向，Vertical 为竖管；自定义路径以 Points 为准 |
| `PipeThickness` | 14 | 管外直径，单位为 WPF 逻辑像素 |
| `WallThickness` | 2 | 管壁厚度，与 Points 使用相同单位 |
| `BendRadius` | 24 | 圆角半径，短段自动缩小；0 保留折角 |
| `WallOpacity` | 0.18 | 管壁不透明度强度，范围 0～1，越小越透明 |
| `FlowColor` | #52D4EF | 水流主色和水纹颜色 |
| `IsFlowing` | false | 流动开关；停流清除液芯和水纹 |
| `FlowSpeed` | 55 | 水纹每秒移动的路径长度；0 暂停，不是实测流量 |
| `ReverseFlow` | false | 反向流动 |
| `IsAnimationEnabled` | true | 动画开关；关闭时冻结水纹，保留液芯 |
| `IsFilled` | true | false 时不显示液芯 |
| `WallColor` | #B4B4B4 | 管壁颜色 |
| `FluidColor` | #C4C4C4 | 液体底色，与 FlowColor 按 1:3 混合 |
| `FluidCoreRatio` | 0.58 | 液芯半径占内腔半径的比例，范围 (0, 1] |

每个控件的路径和流动状态独立。隐藏或卸载后停止动画，重新显示时按当前反馈恢复。
控件不修改绑定源，也不包含设备通信逻辑。改变颜色、速度或状态不会重建路径网格。

普通页面使用 `FluidPipe`；已有共享三维场景的装配代码可以继续使用内部的 `FluidRouteVisual3D`。
底层三维属性使用场景单位，上表的普通控件使用二维页面单位，两者无需在业务页面混用。

## 文件与验证

- 普通控件：`xyz.Core/Client/Common/xyz.Client.Presentation/Controls/FluidPipe.xaml` 和 `FluidPipe.xaml.cs`。
- `Scene.xaml` 只用三个普通控件标签，演示直管、弯管和竖管，没有页面级三维相机和灯光。
- 原生渲染及布局、绑定、集合编辑、冻结、Style、启停和卸载检查：

```powershell
dotnet run --project tools/FluidPipeSmoke -- artifacts/fluid-route-verification/fluid-pipe-control.png
```

命令从 `D:\Code` 执行。测试使用模拟反馈，不连接设备。
