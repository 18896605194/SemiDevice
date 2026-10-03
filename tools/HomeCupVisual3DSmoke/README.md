# HomeCup 三维组件检查

在 Windows 上从仓库根目录执行：

```powershell
dotnet run --project tools/HomeCupVisual3DSmoke
```

可选第一个参数为 PNG 绝对路径，生成实际 WPF 的待机与排液高亮对比图：

```powershell
dotnet run --project tools/HomeCupVisual3DSmoke -- D:\Code\artifacts\homecup-wpf-states.png
```

使用模拟反馈，不显示窗口、不连接硬件。Scene.xaml 提供尺寸和 IsDraining 的绑定示例。
检查贯通中心线、内外壁网格朝向、尺寸绑定和非法值、固定安装位置、多实例独立状态、
排液/选择高亮组合、网格复用和实际渲染蓝色像素变化。失败返回非零退出码。
