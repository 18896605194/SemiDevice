# Robot 框架控件预览

从仓库根目录执行：

```powershell
dotnet run --project tools/RobotControlPreview
```

窗口左侧为原始 `Robot` 2D 控件，右侧为同一个控件的 `Use3D="True"` 外观；
两边使用同一组 `RobotArmModel` / `WaferModel`，都由原 `Robot` 运动轨道驱动。
三维手叉挤出自原 XAML 轮廓，手臂位置使用原 `ArmVisual`，晶圆直接使用 `DiskVisual3D`（内部为 `Wafer`）。
默认视角、俯视、左键拖动和滚轮缩放与现有三维场景一致。
窗口下方直接实例化框架现有 `ChamberScene` / `LoadPortScene`，用于核对组件风格。
所有数据均为本地预览数据，不连接后端或设备。

传入截图路径运行隐藏宿主检查，并输出 2D/3D 直伸式和蛙式对照图：

```powershell
dotnet run --project tools/RobotControlPreview -- D:\Common\artifacts\robot-framework-linear.png
```

检查原动作顺序、晶圆对象与盘面复用、就地状态更新、网格复用、手臂数量变化和隐藏恢复。
主程序当前仍沿用默认 2D，尚未切换调度总览。
