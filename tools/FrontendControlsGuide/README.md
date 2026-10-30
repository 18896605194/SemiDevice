# 框架讲解：前端控件文档生成

只读取平台与项目的客户端 Presentation 程序集和源码，生成 `D:\Code\框架讲解.html` 的“前端 → 控件”。不修改架构源码，不启动后端、不调用 PLC / RPC。

内容包括全部可实例化控件、公开依赖属性（含自定义基类继承项）、类型、默认值、只读 / 默认双向标记、源码注释、手工核实的使用说明和最小示例。
WPF 原生控件截图以及三维网格由实际控件创建后导出，演示数据均为离线样例。浏览器通过本地 WebGL2 展示导出的网格，支持旋转、缩放、预设状态和原生截图对照。网页不承载 WPF 运行时，材质与纹理近似。

```powershell
dotnet run --project D:\Code\tools\FrontendControlsGuide\FrontendControlsGuide.csproj -- D:\Code
node D:\Code\tools\FrontendControlsGuide\build-guide.mjs D:\Code
```

第一步刷新 `docs/frontend-controls/catalog.json`、原生截图和网格。第二步替换 HTML 中带 `FRONTEND-CONTROLS` 标记的生成区域，保留其他章节，更新离线目录和演示数据。
控件说明与最小示例在 `build-guide.mjs`；基础属性补充说明在 `PropertyNotes.cs`。新控件加入 Presentation 后会自动进入导出清单，仍应补齐专门的用法与演示输入。

`docs/frontend-controls` 是 HTML 的本地配套资源目录，移动文档时要一起移动，不需要联网或启动 Web 服务。截图生成使用软件渲染，避免依赖当前桌面的 GPU 状态。

验证命令：

```powershell
node D:\Code\tools\FrontendControlsGuide\verify.mjs D:\Code D:\tools\ToolBuilder\qa\frontend-controls-guide
```

验证直接用 `file://` 打开最终文档，检查目录、锚点、全部三维模型、旋转缩放、属性预设、筛选、输入提交和窄窗口布局。
验证脚本使用当前机器已有的 `playwright-core` / Chromium；其他环境可通过 `PLAYWRIGHT_CORE`（模块绝对路径）和 `CHROMIUM_PATH`（浏览器可执行文件）指定路径。
