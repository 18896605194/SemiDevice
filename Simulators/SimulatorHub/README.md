# xyz 统一仿真器 (SimulatorHub)

一个窗口集中跑多台设备仿真。**每类仿真仍是一个独立 exe 项目**(RejeRobotSimulator / FcdLoadPortSimulator, 单独可跑), 本壳通过项目引用把同一份面板代码当页签用 —— 一个项目里有多台 LoadPort / 多个机械手时, 在主界面按需加实例即可。

## 运行

```
dotnet run --project Simulators/SimulatorHub -- [--data <目录>]
```

`--data` 指定数据根目录 (默认 exe 旁), 下辖:

```
<data>\instances\<实例名>\   每台仿真实例独立的数据目录 (config.json / 应答表 / 日志 / fault.* 旗标)
<data>\profiles\*.json       命名布局预设
<data>\last-layout.json      上次布局 (每次变更即写, 启动自动静默还原)
<data>\last-preset.txt       最近使用的预设名 ([启用预设…] 的目标)
<data>\crash.log             进程级崩溃日志 (整进程一份)
```

## 用法

- **+ 机械手 (TCP)**: 加一台 RejeRobot 实例, 实例目录 `instances\robot-N`, 端口从 9000 起自动避让 (双机械手 → 9000/9001), 加上即开始监听。
- **+ LoadPort (串口)**: 加一台 FcdLoadPort (LP300) 实例, `instances\lp-N`, 串口取实例 config 默认, 加上即尝试打开。
- **页签**: 标题形如 `Robot #2 · 9001`; 双击标题或 [关闭当前页] 关掉该实例 (停监听/关串口, 数据目录保留)。
- **保存布局**: 当前页签组合存成命名预设。
- **启用预设…**: 按最近使用的预设打开勾选对话框 (可只勾部分实例、就地改端口)。
- **载入预设 ▾**: 直接应用上次布局, 或载入/删除已存预设。

启动时若有 `last-layout.json` 会**静默还原**上次布局并按预设的 AutoOpen 自动开口。

## 接入第三台仿真器 (如 PLC)

1. 新建 exe 项目 (照 RejeRobotSimulator 的 csproj: WinExe + net10.0-windows + UseWPF + app.manifest PMv2), 面板做成 `UserControl`, 独立窗口只包面板;
2. 面板暴露宿主契约成员: `string InstanceName`、端口读写、`Open()`(开始服务)、`ShutdownForHost()`(关停); 不挂 Loaded/Unloaded (页签切换会触发 Unloaded), 崩溃钩子只放 App 层;
3. 本项目: csproj 加 ProjectReference, `MainWindow.xaml.cs` 加 `Type*` 常量、工具栏按钮、`CreateTab` 一个分支 (照 Robot/Lp300 的委托样子);
4. `README` 与两个既有仿真器一样保留独立运行说明。

## 设计说明

- 宿主用**委托捕获**各类型 (`SimTab.GetPort/SetPort/Open/Cleanup`), 不建共享接口/MEF —— 每类仿真器保持零公共依赖, 单独构建单独发。
- 生命周期归宿主管: 加页签即启动, 关页签/退出调 `ShutdownForHost()`。
- 布局模型 `LayoutProfile` (System.Text.Json) 与 XM UnifiedSimulator 的 Profile 同构。
- DPI: app.manifest 声明 PerMonitorV2 (混合缩放多屏下布局不糊), 与两个仿真器同款。
