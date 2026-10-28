# Robot3D

普通页面直接使用 `xyz.Client.Presentation.Controls.Robot3D`。它自带相机、灯光和视角操作，
不需要代码后置、ViewModel 注册、服务连接或外部 `Viewport3D`。

```xml
xmlns:controls="clr-namespace:xyz.Client.Presentation.Controls;assembly=xyz.Client.Presentation"

<controls:Robot3D
    Height="340"
    ArmType="FrogLeg"
    ArmCount="{Binding Robot.ArmCount}"
    Arms="{Binding Robot.Arms}"
    Rotation="{Binding Robot.Direction}"
    Travel="{Binding Robot.Y}" />
```

直伸式用 `ArmType="Linear"`，其余调用保持不变。最小写法为 `<controls:Robot3D />`，
默认显示两只收回的直伸式空手。控件继承页面的 `DataContext`，不会覆盖它。

## 与二维相同的属性

| 属性 | 含义 | 默认值 |
| --- | --- | --- |
| `ArmType` | `Linear` 直伸式 / `FrogLeg` 蛙式 | `Linear` |
| `ArmCount` | 1～4 只手臂，超出范围按二维规则修正 | `2` |
| `Arms` | `IEnumerable<RobotArmModel>`，按 `Arm` 手指号对应 | 空手占位 |
| `Rotation` | `RobotDirection`：北 0 / 东 90 / 南 180 / 西 270 | 北 |
| `Travel` | 水平平移，沿用二维设计坐标，负数向左 | `0` |
| `ExtendedReach` | 完全伸出时片心到转台中心的距离 | `150` |
| `IsAnimationEnabled` | 状态过渡动画；关闭后直接落到目标 | `true` |

`RobotArmModel.Extension` 在 0～1 间控制各手臂伸缩，`Heading` 保留相对转台的朝向偏置，
`Wafer` 直接使用原 `WaferModel`。集合增删、换源、手指号变化和晶圆状态 / 片号就地更新都受支持。
缺失手指用空手占位，同号采用第一条，超出 `ArmCount` 的不显示。

`Travel` 保持二维接口的平移语义，**不是三维高度**；上例 `Robot.Y` 对应当前二维调度图的绑定。
世界坐标用现有晶圆盘直径统一比例，原 84 设计单位的晶圆对应 `DiskVisual3D` 的 2.64 场景单位。
三维厚度与臂间高度是外观示意，不增加实际 Z 轴或控制逻辑。

## 视角和生命周期

左键拖动旋转，滚轮缩放；默认提供“默认视角”和“俯视”。
设 `ShowViewToolbar="False"` 可只显示机械手，仍保留拖动和缩放。
代码中可调用 `ResetView()`、`ShowTopView()`；这些操作只改相机，不改变姿态或绑定。

二维和三维都由 `RobotViewBase` 维护原运动轨道：收回 → 转向 / 平移 → 伸出，换手先收完再伸出，
每段 0.5 秒，中途换目标从当前姿态继续。隐藏时直接落到当前目标，不保留逐帧工作；
移除控件后解除集合、模型和渲染事件，重新装入时恢复当前输入。

`Styles/RobotResources.xaml` 保存原二维色板、手叉轮廓和转台圆角轮廓。
三维材质沿用原色板，几何冻结后通过变换更新；晶圆使用已有 `DiskVisual3D` 和它内部的 `Wafer`，
保持原状态色和片号。独立几何组件位于 `ThreeD/RobotVisual3D.cs`，可供未来共享场景装配使用。

验证与可编译的最小 XAML 示例见 `tools/Robot3DSmoke`。本次未替换主界面调度总览。
