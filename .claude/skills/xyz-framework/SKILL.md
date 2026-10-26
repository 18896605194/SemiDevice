---
name: xyz-framework
description: xyz 半导体设备框架（D:\Code）的架构分层和编码规范——后端组件/模块/驱动/gRPC 服务/配置/数据库，WPF 客户端 MVVM/样式/语言包，机型层，冒烟测试，验证和交付方式。在这个仓库里写或改任何代码（C#、XAML、machine.xml、冒烟测试）之前先读；加组件、模块、动作、服务、推送、页面、菜单、报警、machine.xml / ec.xml 参数、错误码、数据表时按里面的清单做。
---

# xyz 框架开发规范

本文件是总纲：架构、硬规矩、按任务查哪份参考、怎么验证和交付。细节在 `references/`：

| 文件 | 内容 |
|---|---|
| `references/backend.md` | 组件、machine.xml / ec.xml 参数、报警、模块和动作、驱动、gRPC 服务、事件推送、错误码、数据库、配置文件、日志、线程 |
| `references/client.md` | 客户端工程、启动、菜单和页面、ViewModel、View、样式、控件、语言包、显示模型、旧代码里的不一致 |
| `references/machine-and-tools.md` | 仓库顶层、机型层和部署、冒烟测试、把软件跑起来看、界面离屏预览 |
| `references/decisions.md` | 已经跟用户定下来的设计，改相关功能前先看，不要改回去 |

## 一、架构

```
xyz.Core\Shared   契约 xyz.Shared（服务接口 Services\、DTO Dtos\、错误码 Errors\）+ 工具 xyz.Tools（EventBus、JsonHelper、IocHelper、ExcelHelper，前后端共用的帮助类都放这里）
xyz.Core\Service  后端：xyz.Components（组件）→ xyz.Modules（模块）→ xyz.Service（gRPC 服务、装配、事件桥）→ xyz.GrpcHost（宿主）
                  旁支：xyz.Drivers（通讯、设备通道、指令底座；品牌协议不在这里）、xyz.Secs（跟主机、厂里系统通讯的底层：SECS/HSMS、GEM 各标准协议、EDA 引擎；单独仓库 D:\Secs\xyz.Secs，框架只引 Libs\xyz.Secs.dll）、xyz.Database（SqlSugar）、xyz.Configs（读 machine.xml，文件本身归机型）、xyz.Common（日志；后端自己用的帮助类：时间换算 DateTimeHelper、值换算 ValueHelper）
xyz.Core\Client   客户端：xyz.Client（壳）→ 功能模块 Main/Alarm/DataCenter/Io/Manual/Recipe/Setting → Common\xyz.Client.Presentation（控件/样式/语言包）
                  → Common\xyz.Client.Common（RPC/事件/日志）、Common\xyz.Client.DataModels（BaseViewModel）；Common\xyz.Client.Modules（机型模块接口）
xyz.35021         机型层：继承平台 Base*Module 写具体设备（品牌协议、指令在 Drivers\<品牌>）、机型客户端页面、machine.xml 和 IO 点表；编译后部署到宿主和客户端的 Modules\35021（machine.xml、点表进宿主 Config），运行时扫描加载
tools             冒烟测试（控制台程序，不是单元测试工程）、部署和编码脚本
```

- 客户端和后端之间只有两条路：gRPC（protobuf-net code-first，契约在 xyz.Shared）和远程事件流（后端 `EventBus.Send` → 客户端 `EventBus.Register`）。
  gRPC 请求一律 `RpcRequest`、回包一律 `RpcResponse`，不建请求类（2026-10-26 用户定）：参数按名字装在 `RpcRequest.Parameters`（JSON 对象）里，
  名字只写一份：`xyz.Shared.Rpc.RpcParams` 里的常量，客户端装、服务端取都用它，不在两头各写字符串（新参数名先加进去）。
  客户端 `RpcRequest.Of((RpcParams.Index, 1))`，服务端 `request.TryGet<int>(RpcParams.Index, out var index)`（统一写 `TryGet<类型>(名字, out var 变量)`，字符串、列表写 `out string x` 会报可能为 null）/
  `request.Get(RpcParams.Keyword, "")`；契约注释一句短话，不写参数（2026-10-26 用户改）。
  服务都继承 `BaseService`，回包用它的 `Fail` / `NotInstalled` / `Reply` / `ReplyData` / `InvalidRequest`，不在服务里各写私有的（见 backend.md §5「返回」）。
- 设备树：machine.xml 一个节点 = 一个组件实例（`Type` 写类型全名），模块是带状态和动作的组件；后端启动时装配整棵树，按开机几个阶段走：
  组件初始化（`InitComponent`）→ `Bind` → 开始扫描（machine.xml 配了 `IsRun` 的根）→ `OnStarted`，退出倒着 `OnStopping`。组件自己的开机准备、收尾、推送
  都写在这几个钩子和自己的 `OnScan` 里；启动代码（`ServiceExtensions`、宿主 `Program`）只做框架的事、不写具体组件，加组件不用改它们。
- 设备动作 = `ModuleRoutine`（按顺序走的一串有名字的步骤，平台建动作、机型往里加步骤），在模块扫描线程里一步步推进；RPC 线程只发起和等待。

### 依赖铁律
1. 客户端不引用任何后端工程，只用 xyz.Shared 的契约。
2. xyz.Components 引用 xyz.Shared（2026-10-05 起，用户定的；原来是不引用）：错误码（模块动作 `ModuleRoutine` 挪到了组件层）、设备侧给 EAP 的接口
   （`Interfaces` 下的 ILoadPortAccess、IJobManager 和回调口）用到的 DTO，和组件自己推给界面的 DTO——**推送是组件自己的事**（2026-10-18 起）：
   组件在自己的扫描或钩子里 `EventBus.Send`，转 DTO 走映射组件（`IMapper`，平台规则写在 xyz.Modules 的 `MapperComponent.AddRules`，项目继承它重写 `AddRules` 加自己的），不在启动代码里搭桥。
   组件层的内部成员只对 xyz.Modules 开放（InternalsVisibleTo）。
3. 平台不引用机型；机型能用平台抽象的就用（继承 Base*Module、实现 I*），平台缺抽象就补到平台。
4. 客户端功能模块之间不互相引用；共用的下沉到 Presentation / Common。xyz.Drivers 不引用 Components。
5. **组件都在 IoC 容器里，找组件一律按接口、从 `IocHelper` 取**（2026-10-18 用户定的，不按基类、不按具体类）：装配器照 machine.xml 树把每个组件
   按它实现的 xyz 接口登记进容器（`ComponentLoader.Register`），用的地方 `IocHelper.GetService<IXxx>()`（取一个，没装是 null）、
   要一类的全部（所有 LoadPort、所有腔体）用 `IocHelper.GetServices<IXxx>()`（按 machine.xml 先后，停用的连同下面的跳过）。不在组件基类上另包取法，
   不写 `Xxx.Current`、不在构造里登记、不往 Bind 里传整棵树；**组件里用到的单例开机时取一次存字段**（`Bind` 里取，开机准备就要用的在
   `InitComponent` 里取），不在每个方法里现取；服务在构造里取。模块共有的走 `IModule`，跟机械手交互的站点 `ITransferStation`、不交互的（供液）`ISupplyModule`；
   能做工艺的站点 `IProcessStation`（带 `Type`，配方、流程配方、Job 认它），腔体手动页那些在继承它的 `IChamber` 里。
   **模块要用另一个模块**（2026-10-20 用户定的，比如腔体起工艺要看供液就绪）：在 machine.xml 上直接配对方的模块名，`Bind` 里按名字从
   `IocHelper.GetServices<对方专门给别人用的接口>()` 挑出来存字段，配了却找不到就抛（开机暴露）；不靠药液名之类隐式对号，
   只经那个接口读对方自己算好的结论，不碰对方的子组件、不在扫描线程等对方、不拿对方的锁。供液给别人用的是 `ISupplyAccess`（腔体 machine.xml `Supply` 配模块名）。

## 二、硬规矩（所有 C#、XAML，包括 tools 下的冒烟）

1. **注释中文、大白话、讲为什么**；成员写 `/// <summary>`，**一律分三行**（`/// <summary>`、`/// 一句短话`、`/// </summary>`），
   不写成一行的 `/// <summary>xxx</summary>`；内容一句短话说清是什么，长的背景写进 decisions.md（用户 2026-10-16 定）。日志、异常信息、界面文字也是中文（英文只出现在 en-US 语言包）。
2. **if、switch 只用老写法**（用户 2026-10-16、10-18 定的，新写的、改到的都照这个）：
   - **switch 用传统 switch 语句**：`case A:` 下面 `return …;`，或者赋值再 `break;`，最后 `default:`。**不写 switch 表达式**（`x switch { A => 1, _ => 0 }`），
     case 里也不写 `or`、`when`、关系模式这些新写法——几个值结果一样就把 case 叠起来写，带条件的改成 if；
     要给变量赋值的先声明、在 case 里赋值；写在对象初始化器、实参、lambda 里的抽成一个 `private static` 换算方法。
   - **if 的大括号一定单独一行**：`if` / `else` / `for` / `foreach` / `while` / `lock` / `try` / `catch` 后面哪怕只有一句 `return` / `continue` / `break` / `throw` / 赋值，
     也要加大括号，`{`、`}` 各占一行。不写 `if (x) return;`、`if (!ok) throw ...;`，也不写 `if (x) { return; }`、`try { X(); }`、
     `get { lock (_gate) { return _x; } }` 这种挤在一行的；lambda 里的 `if` 也一样。
   ```csharp
   switch (format)
   {
       case SecsFormat.I1:
       case SecsFormat.U1:
           return 1;
       case SecsFormat.I2:
       case SecsFormat.U2:
           return 2;
       default:
           return 8;
   }

   if (version != _transitionVersion)
   {
       return;
   }
   ```
3. **不写花括号模式匹配和列表模式**：`x is { } y`、`is not { } y`、`is { Count: > 0 }`、`is [..]` 都不要。
   先取局部变量，再显式判空、判属性；**判空只用 `is null` / `is not null`**，不写 `== null` / `!= null`。if 条件里类型模式（`x is Foo foo`）、常量和 `or` 模式可以用。
   ```csharp
   var alarms = IocHelper.GetService<IAlarm>();
   if (alarms is not null && alarms.ActiveAlarms.Count > 0)
   {
       ...
   }
   ```
4. **不留魔法数**：装机、接线、结构性的进 machine.xml（`[Machine]`）；现场要调的（超时、防抖、周期、批量）进 ec.xml（EC `[VariableMark]`）；协议常量写 `const`。
5. 文件级命名空间；私有字段 `_camelCase`；常量 PascalCase；目录 = 命名空间（组件 `Components\<类别>` 例外）。
6. 源文件（.cs、.xaml、.csproj、.xml）UTF-8 **带 BOM**；含中文的 PowerShell 5.1 脚本也要带 BOM。skill 的 .md 不加 BOM（frontmatter 要从第一个字节开始）。
7. 线程：共享状态 `lock (_gate)`，事件在锁外发、给调用方副本；扫描线程里不等待；后台写库走单读者 `Channel`。
   **字段不先接到局部变量再用**（用户 2026-10-27 定）：开机后不变的字段（构造、`InitComponent`、`Bind`、`Attach` 里赋值的，readonly 的）直接 `if (_x is null)`、`_x.Foo()`，
   不写 `var x = _x;`；只有运行中会被别的线程改掉的（断线重连换掉的连接、Attach / Detach 的引擎、在做的动作）才先接一遍再判空、使用。
8. 错误：配置/装配错了抛 `InvalidOperationException`（开机就暴露）；运行期返回结果（bool、结果枚举、操作的 Code+Args）；
   服务层翻成 `RpcResponse.Fail(ErrorCodes.X, [参数])` 给界面，**不向客户端抛异常**；界面按错误码查语言包显示。
9. 用户看得到的文字都走语言包，**zh-CN、en-US 两份的 key 一一对应**，加一条两边都加。
10. 客户端：模块状态用 `ModuleStateBadge`；输入用 `InputTextBox`（绑 Value）；样式只放 `Presentation\Styles`，颜色、字号用 token（`Dark*`、`SizeNN`）；页面里不写资源、不写死颜色和字号。
11. 删功能要删干净：界面、逻辑、语言包、样式、DTO 字段、测试一起删，不留没人用的代码。
12. **后端记日志一律走 `LogHelper`**（用户 2026-10-25 定）：不写 `Console.WriteLine`、`ILogger`、`LogManager.GetLogger`；组件里写 `LogHelper.Info(this, …)`（两层名字：模块、部件），
    不是组件的写固定名字（`LogHelper.HostModule`）；第三方库的日志用 `LogHelperLoggerProvider` 转进来。客户端照旧 `ClientLog`。见 backend.md §9。
13. **定时查设备状态一律写在模块的 `QueryDataLoop` 里，交给 `StatusQueryLoop` 发**（2026-10-26）：别在扫描里自己定时 `Send`——它发的指令打 `IsPolling`，
    通道才知道把收发原文记到轮询文件，不然会刷满主日志。
14. **时间换算、值换算用 `xyz.Common` 的 `DateTimeHelper`、`ValueHelper`**（用户 2026-10-26 定）：各文件里不写私有的换算小方法（本机时间 ↔ UTC 毫秒、`double?` → `float?`、
    字符串按类型转值这类），缺的加进这两个类；前后端都用的帮助类放 `xyz.Tools`。见 decisions.md「后端帮助类」。

## 三、按任务找做法

| 要做的事 | 看哪里 |
|---|---|
| 加组件、machine.xml / ec.xml 参数、报警、GEM 事件 | backend.md §2 |
| 加模块、加动作（ModuleRoutine） | backend.md §3；机型实现见 machine-and-tools.md §2 |
| 加品牌驱动、改通讯 | backend.md §4 |
| 加 gRPC 服务或接口 | backend.md §5 清单（契约（注释一句短话、不写参数）→ 新参数名加进 RpcParams → 实现（TryGet 取参数）→ 错误码 → 语言包 → 冒烟；不建请求类、不用注册） |
| 后端推送、客户端订阅 | backend.md §5「事件推送」；client.md §4「事件」 |
| 加错误码 | backend.md §6（+ 两个语言包） |
| 加表、流水、历史清理 | backend.md §7 |
| 改 machine.xml、启动顺序 | backend.md §5「启动顺序」、§8 |
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
   switch 新写法也要搜：switch 表达式 `rg -U "[\w)\]]\s+switch\s*\r?\n?\s*\{" --glob "*.cs"`，case 里的新模式 `rg "^\s*case\b.*\b(or|when)\b" --glob "*.cs"`。
   找组件的老写法也要搜：`rg "ComponentBase\.(Find|FindAll|Register|Roots)|\bRegister<I\w+>\(this\)|\w(Component|Collectors|Module|Manager)\.Current\b" --glob "*.cs"` 应该一条都没有。

## 五、跟用户的协作方式

- 大的界面或方向先对齐：先出 HTML 样稿 / 截图给用户看，确认了再正式开发。
- 方向定了就一次做完再交付，中途不一块一块停下来汇报；能自己定的细节自己定，交付时说明。
- 交付时如实说：做了什么、怎么验证的（编译结果、冒烟检查数、截图位置）、哪些没验证到、已知问题、要用户拍板的事。
- 用户说"先不做"的不做；用户定过的设计不改回去（decisions.md）。
- 不自作主张提交、推送（提交由用户自己做）；不改用户没让改的配置；临时改运行目录的配置（比如切英文看界面）用完立刻改回。
