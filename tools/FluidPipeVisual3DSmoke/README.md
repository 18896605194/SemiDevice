# Arm 双喷嘴与透明水流检查

腔体的 `FluidPipeVisual3D` 已改为复用普通 `FluidPipe` 控件使用的 `FluidRouteVisual3D` 绘制。
每路喷嘴由透明供液管、向下圆角和外部液柱组成；DIW、SC1 分别绑定自己的 `IsFlowing`。
外部液柱只有水流，没有管壁，落点使用较淡的细波纹。

现有 `Length`、`StreamLength`、`OutletDrop` 和 Arm.Attachments 的装配接口继续使用：
腔体的摆臂转向、Lift 升降、盘面及 Home 接液杯落点计算直接作用于新效果。
`FlowColor` 控制管内液芯、喷口液柱和波纹；`WallOpacity` 控制透明供液管的管壁。
流动时管壁保持中性颜色；停流清除液芯、外部液柱和落点波纹。
`IsAnimationEnabled=false` 暂停全部动画并保留液体。

在 `D:\Code` 执行：

```powershell
dotnet run --project tools/FluidPipeVisual3DSmoke -- artifacts/fluid-route-verification/arm-two-transparent-nozzles.png
dotnet run --project tools/ChamberSceneSmoke -- artifacts/fluid-route-verification/chamber-transparent-nozzles.png
```

测试使用隐藏的原生窗口宿主和模拟反馈，不连接硬件。第一张图显示两路同时出液的 Arm 近景，
第二张图显示实际 ChamberScene 中的工艺位和 Home 位出液。

喷嘴检查覆盖实际水纹像素变化、冻结网格复用、两路独立启停、停流恢复空管像素、
Arm 转向和 Lift 升降继承、隐藏/移除时全部子动画停止、暂停、绑定保留、颜色、
喷液长度变化只更新液柱、非法尺寸和零外部液柱。整腔体检查覆盖设备推送、装配和实际落点。
