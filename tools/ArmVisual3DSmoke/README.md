# Arm 三维组件检查

在 Windows 上从仓库根目录运行：

```powershell
dotnet run --project tools/ArmVisual3DSmoke
```

可选第一个参数为输出 PNG 的路径，生成真实 WPF 静止/动作高亮对比图：

```powershell
dotnet run --project tools/ArmVisual3DSmoke -- D:\Code\artifacts\arm-wpf-states.png
```

使用 STA 和独立的模拟反馈源，不显示窗口、不连接硬件。失败返回非零退出码。
`Scene.xaml` 是可编译的最小集成示例，使用项目的暗色主题资源。
检查依赖属性绑定与反馈源切换、几何和材质、旋转方向、Lift 独立性、停止恢复、选中状态、
实例隔离、主题更新、输入有效性；指定 PNG 时还验证高亮实际改变三维区域像素。
