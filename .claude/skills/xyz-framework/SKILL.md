---
name: xyz-framework
description: xyz 半导体设备框架（D:\Code）的架构分层和编码规范——后端组件/模块/驱动/gRPC 服务/配置/数据库，WPF 客户端 MVVM/样式/语言包，机型层，冒烟测试，验证和交付方式。在这个仓库里写或改任何代码（C#、XAML、sc.xml、冒烟测试）之前先读；加组件、模块、动作、服务、推送、页面、菜单、报警、SC/EC 参数、错误码、数据表时按里面的清单做。
---

# xyz 框架开发规范

本文件是总纲：架构、硬规矩、按任务查哪份参考、怎么验证和交付。细节在 `references/`：

| 文件 | 内容 |
|---|---|
| `references/backend.md` | 组件、SC/EC 参数、报警、模块和动作、驱动、gRPC 服务、事件推送、错误码、数据库、配置文件、日志、线程 |
| `references/client.md` | 客户端工程、启动、菜单和页面、ViewModel、View、样式、控件、语言包、显示模型、旧代码里的不一致 |
| `references/machine-and-tools.md` | 仓库顶层、机型层和部署、冒烟测试、把软件跑起来看、界面离屏预览 |
| `references/decisions.md` | 已经跟用户定下来的设计，改相关功能前先看，不要改回去 |

## 一、架构

```
xyz.Core\Shared   契约 xyz.Shared（服务接口 Services\、DTO Dtos\、错误码 Errors\）+ 工具 xyz.Tools（EventBus、JsonHelper、IocHelper）
xyz.Core\Service  后端：xyz.Components（组件）→ xyz.Modules（模块）→ xyz.Service（gRPC 服务、装配、事件桥）→ xyz.GrpcHost（宿主）
                  旁支：xyz.Drivers（通讯/协议）、xyz.Secs（SECS/HSMS）、xyz.Database（SqlSugar）、xyz.Configs（sc.xml）、xyz.Common（日志）
xyz.Core\Client   客户端：xyz.Client（壳）→ 功能模块 Main/Alarm/DataCenter/Io/Manual/Recipe/Setting → Common\xyz.Client.Presentation（控件/样式/语言包）
                  → Common\xyz.Client.Common（RPC/事件/日志）、Common\xyz.Client.DataModels（BaseViewModel）；Common\xyz.Client.Modules（机型模块接口）
xyz.35021         机型层：继承平台 Base*Module 写具体设备、机型客户端页面、IO 点表；编译后部署到宿主和客户端的 Modules\35021，运行时扫描加载
tools             冒烟测试（控制台程序，不是单元测试工程）、部署和编码脚本
```

- 客户端和后端之间只有两条路：gRPC（protobuf-net code-first，契约在 xyz.Shared）和远程事件流（后端 `EventBus.Send` → 客户端 `EventBus.Register`）。
- 设备树：sc.xml 一个节点 = 一个组件实例（`Type` 写类型全名），模块是带状态和动作的组件；后端启动时装配整棵树、按顺序 Open/Start。
- 设备动作 = `ModuleOperation`，在模块扫描线程里一步步推进；RPC 线程只发起和等待。

### 依赖铁律
1. 客户端不引用任何后端工程，只用 xyz.Shared 的契约。
2. xyz.Components 引用 xyz.Shared 只为两样（2026-10-05 起，用户定的；原来是不引用）：错误码（`ModuleOperation` 挪到了组件层），
   设备侧给 EAP 的接口（`Interfaces` 下的 ILoadPort、IJobManager 和回调口）用到的 Job DTO。组件发 C# 事件转成 EventBus 推送的桥照旧搭在
   `xyz.Service\ServiceExtensions.cs`，组件里不直接推客户端。组件层的内部成员只对 xyz.Modules 开放（InternalsVisibleTo）。
3. 平台不引用机型；机型能用平台抽象的就用（继承 Base*Module、实现 I*），平台缺抽象就补到平台。
4. 客户端功能模块之间不互相引用；共用的下沉到 Presentation / Common。xyz.Drivers 不引用 Components。

## 二、硬规矩（所有 C#、XAML，包括 tools 下的冒烟）

1. **注释中文、大白话、讲为什么**；成员写 `/// <summary>`。日志、异常信息、界面文字也是中文（英文只出现在 en-US 语言包）。
2. **不写花括号模式匹配和列表模式**：`x is { } y`、`is not { } y`、`is { Count: > 0 }`、`is [..]`、switch 里 `{ Prop: v } =>` 都不要。
   先取局部变量，再显式判空、判属性；**判空只用 `is null` / `is not null`**，不写 `== null` / `!= null`。类型模式（`x is Foo foo`）、常量和 `or` 模式可以用。
   ```csharp
   var alarms = AlarmComponent.Current;
   if (alarms is not null && alarms.ActiveAlarms.Count > 0) { ... }
   ```
3. **不留魔法数**：装机、接线、结构性的进 sc.xml（`[SCEditor]`）；现场要调的（超时、防抖、周期、批量）进 ec.xml（EC `[VariableMark]`）；协议常量写 `const`。
4. 文件级命名空间；私有字段 `_camelCase`；常量 PascalCase；目录 = 命名空间（组件 `Components\<类别>` 例外）。
   **大括号一律不省略**：`if` / `else` / `for` / `foreach` / `while` / `lock` 后面哪怕只有一句 `return` / `continue` / `break` / `throw` / 赋值，
   也要换行加大括号；不写 `if (x) return;`、`if (!ok) throw ...;` 这种单行，lambda 里的 `if` 也一样。
   带了大括号也不挤在一行：`if (x) { return; }`、`try { X(); }`、`catch { ... }`、`get { lock (_gate) { return _x; } }` 都拆成多行。
   ```csharp
   if (version != _transitionVersion)
   {
       return;
   }
   ```
5. 源文件（.cs、.xaml、.csproj、.xml）UTF-8 **带 BOM**；含中文的 PowerShell 5.1 脚本也要带 BOM。skill 的 .md 不加 BOM（frontmatter 要从第一个字节开始）。
6. 线程：共享状态 `lock (_gate)`，事件在锁外发、给调用方副本；扫描线程里不等待；后台写库走单读者 `Channel`。
7. 错误：配置/装配错了抛 `InvalidOperationException`（开机就暴露）；运行期返回结果（bool、结果枚举、操作的 Code+Args）；
   服务层翻成 `RpcResponse.Fail(ErrorCodes.X, [参数])` 给界面，**不向客户端抛异常**；界面按错误码查语言包显示。
8. 用户看得到的文字都走语言包，**zh-CN、en-US 两份的 key 一一对应**，加一条两边都加。
9. 客户端：模块状态用 `ModuleStateBadge`；输入用 `InputTextBox`（绑 Value）；样式只放 `Presentation\Styles`，颜色、字号用 token（`Dark*`、`SizeNN`）；页面里不写资源、不写死颜色和字号。
10. 删功能要删干净：界面、逻辑、语言包、样式、DTO 字段、测试一起删，不留没人用的代码。

## 三、按任务找做法

| 要做的事 | 看哪里 |
|---|---|
| 加组件、SC/EC 参数、报警、GEM 事件 | backend.md §2 |
| 加模块、加动作（ModuleOperation） | backend.md §3；机型实现见 machine-and-tools.md §2 |
| 加品牌驱动、改通讯 | backend.md §4 |
| 加 gRPC 服务或接口 | backend.md §5 清单（契约 → DTO → 实现 → 两处注册 → 错误码 → 语言包 → 冒烟） |
| 后端推送、客户端订阅 | backend.md §5「事件推送」；client.md §4「事件」 |
| 加错误码 | backend.md §6（+ 两个语言包） |
| 加表、流水、历史清理 | backend.md §7 |
| 改 sc.xml、启动顺序 | backend.md §5「启动顺序」、§8 |
| 加客户端页面、菜单 | client.md §3 清单 |
| 改主界面、按机型换整机调度图、LoadPort 页签 | client.md §3「主界面」；机型注册见 machine-and-tools.md §2 |
| 写 ViewModel / View | client.md §4、§5 |
| 加样式、用控件 | client.md §6、§7 |
| 加文字 | client.md §8 |
| 机型代码、点表、部署 | machine-and-tools.md §2 |
| 写冒烟测试 | machine-and-tools.md §3 |

## 四、验证（交付前都要做完）

1. 编译整个 `xyz.Framework.sln`：0 错误（`tools\IoIndexSmoke` 不在 sln 里，单独编）。宿主、客户端在跑时先关掉，否则 DLL 拷不进去。
2. 跑冒烟：至少跑相关的；动了公共代码（组件基类、模块基类、契约、事件、日志）就全跑一遍，对一下每个的检查数。**新功能、改了行为要在冒烟里加检查**。
3. 改了界面：用离屏预览出中英文截图看一遍（machine-and-tools.md §5）；用户要看就把软件真跑起来（§4）。
4. 全仓再搜一遍硬规矩（如 `rg "\bis\s+(not\s+)?[\{\[]" --glob "*.cs"`、`== null`）。大括号三种写法都要搜：
   单行不带括号 `rg "^\s*(if|else if|while|for|foreach)\b.*\)\s*[^{\s/].*;\s*$" --glob "*.cs"`；
   换行不带括号 `rg -U "^\s*(if|else if|while|for|foreach|lock)\b.*\)\s*\r?\n\s*[A-Za-z_]" --glob "*.cs"`、`rg -U "^\s*else\s*\r?\n\s*[A-Za-z_]" --glob "*.cs"`；
   带了括号却挤在一行 `rg "^\s*(if|else if|else|while|for|foreach|lock|try|catch|finally)\b[^{]*\{[^}]*\}\s*$" --glob "*.cs"`。

## 五、跟用户的协作方式

- 大的界面或方向先对齐：先出 HTML 样稿 / 截图给用户看，确认了再正式开发。
- 方向定了就一次做完再交付，中途不一块一块停下来汇报；能自己定的细节自己定，交付时说明。
- 交付时如实说：做了什么、怎么验证的（编译结果、冒烟检查数、截图位置）、哪些没验证到、已知问题、要用户拍板的事。
- 用户说"先不做"的不做；用户定过的设计不改回去（decisions.md）。
- 不自作主张提交、推送（提交由用户自己做）；不改用户没让改的配置；临时改运行目录的配置（比如切英文看界面）用完立刻改回。
