# Lift 三维气缸检查

在 Windows 上从仓库根目录运行：

```powershell
dotnet run --project tools/LiftVisual3DSmoke
```

可选第一个参数为输出 PNG 的路径，生成真实 WPF 的下位、上升中、上位对比图：

```powershell
dotnet run --project tools/LiftVisual3DSmoke -- D:\Code\artifacts\lift-wpf-states.png
```

使用 STA、WPF 动画时钟和模拟布尔反馈，不显示窗口、不连接硬件；失败返回非零退出码。
`Scene.xaml` 展示仅绑定 `IsRaised` 的最小集成方式。
检查初始状态、两个方向和中途反向、自动动作高亮及停止恢复、选中和外部动作反馈独立性、
布尔绑定保留、MountHeight 装配绑定、Arm 回 Home 独立性、固定缸体及网格复用。
指定 PNG 时还验证只有过渡中的模型渲染为蓝色。
