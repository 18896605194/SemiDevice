# 管子流动与装配检查

在 Windows 上从仓库根目录运行：

```powershell
dotnet run --project tools/FluidPipeVisual3DSmoke
```

可选第一个参数为实际 WPF 渲染 PNG 的绝对路径：

```powershell
dotnet run --project tools/FluidPipeVisual3DSmoke -- D:\Code\artifacts\fluid-pipe-wpf.png
```

使用隐藏的原生窗口宿主、STA 和模拟反馈，不连接硬件；失败返回非零退出码。
Scene.xaml 展示 Lift 安装高度绑定，以及 Arm.Attachments 中两根独立管子的最小装配。
管子的 IsFlowing 分别绑定 DiwIsFlowing、Sc1IsFlowing；Length 绑定 Arm.Length。
StreamLength 使用默认示意距离，完整腔体需按实际接液面更新。

覆盖实际流动像素变化、亮段位移、冻结网格复用、通道独立性、Arm 转向和 Lift 升降继承、
Home 不改变升降和出液、页面隐藏/恢复、整臂和单管拆卸/重新挂载、动画暂停、
关闭后清除液体、绑定保留、尺寸校验及零外部液柱。
输出 PNG 是动画某一帧，不能单凭静态图片观察流速。
