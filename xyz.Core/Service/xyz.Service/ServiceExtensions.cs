using Microsoft.Extensions.DependencyInjection;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Collectors;
using xyz.Components.Components;
using xyz.Configs;
using xyz.Configs.Models;
using xyz.Modules;
using xyz.Service.Alarms;
using xyz.Service.Charts;
using xyz.Service.Events;
using xyz.Service.Recipes;
using xyz.Service.Systems;
using xyz.Service.UserManger;
using xyz.Service.Wafers;
using xyz.Shared.Dtos;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service;

/// <summary>
/// 业务服务注册扩展。
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// 注册 xyz 后端业务服务。
    /// </summary>
    public static IServiceCollection AddXyzServices(this IServiceCollection services)
    {
        #region 日志转发（LogHelper → 队列 → 单消费者 → EventBus → 客户端）

        LogQueue.Start(item =>
        {
            var log = new LogDto
            {
                Time = item.Time,
                Level = item.Level.Name,
                Module = item.Module,
                Message = item.Message,
                Source = "Server",
            };

            // 进环形缓冲（供客户端连上后补历史）+ 实时推事件流
            LogHistory.Add(log);
            EventBus.Send(log, LogDto.EventToken, retain: false);
        });

        #endregion

        #region 模块装配（SC 配置 → 组件实例 → EC 合并 → 启动扫描线程）

        var settings = SC.Load();
        services.AddSingleton<IReadOnlyList<ModuleConfig>>(settings);

        // 驱动接入后在此处补 Open（先连接后启动）。
        var roots = ComponentLoader.Load(settings);
        services.AddSingleton<IReadOnlyList<ComponentBase>>(roots);

        // EC 组件把组件树 [VariableMark(EC)] 声明合并进 ec.xml（没有这个文件就生成，缺的补建，已有值不动，层级先后跟 sc.xml 一样）。
        var ec = EcComponent.Current;
        if (ec is not null)
        {
            ec.Merge(roots);

            // EC 值一变就推给客户端（界面改的、组件自己写的都算）：EC 设置页和输入框按 EcKey 取的范围跟着走。
            // EC 组件在组件层（不引用契约层），所以这条桥搭在这儿，跟报警那条一个路子。
            ec.ValueChanged += (path, value) => EventBus.Send(value.ToDto(path), EcItemDto.EventToken, retain: false);
        }
        else
        {
            LogHelper.Warn("EC", "sc.xml 没配 EC 节点：EC 参数全按代码默认值走，改了也不落盘");
        }

        // 编号：EC/SV/报警/CEID/DV 五个采集器按代码声明生成编号表（EcDefinitions.xml 等，跟 sc.xml 同目录）：
        // 已有的保号、新增的在号段里接着分、代码里删掉的停用保号；报警另配报出/清除事件；一键采集走 CollectAll。
        var collectors = new GemCollectors();
        collectors.Merge(roots, SC.ConfigDirectory);
        services.AddSingleton(collectors);

        // 报警转推客户端：报警组件在组件层（不引用契约层），所以这条桥搭在这儿，跟日志那条一个路子。
        var alarms = AlarmComponent.Current;
        if (alarms is not null)
        {
            alarms.AlarmChanged += item => EventBus.Send(item.ToDto(), AlarmDto.EventToken, retain: false);
        }

        // 晶圆账一变就通知客户端：只说哪个位置变了，不带账（整篮 Mapping 会一下来一串），账单调整页收到后合并着重拉一次。
        var wafers = WaferManager.Current;
        if (wafers is not null)
        {
            void NotifyLedger(string module) =>
                EventBus.Send(new WaferLedgerChangedDto { Module = module }, WaferLedgerDto.EventToken, retain: false);

            wafers.WaferCreated += wafer => NotifyLedger(wafer.Module);
            wafers.WaferDeleted += wafer => NotifyLedger(wafer.Module);
            wafers.WaferUpdated += wafer => NotifyLedger(wafer.Module);
            wafers.WaferMoved += (wafer, _, _) => NotifyLedger(wafer.Module);
        }

        // EAP 主机链路（sc.xml 的 Hsms 节点）：排在编号表合并之后——S1F3 要按 SVID 表答话；
        // IsEnable=False 时组件自己只记一条日志不监听。退出时的 Separate 优雅断开挂在宿主的 ApplicationStopping。
        foreach (var hsms in roots.OfType<HsmsComponent>())
        {
            hsms.Open();
        }

        // PLC 是全机 IO 底座（气缸的 DI/DO、轴的数据块都从它走），所以先于模块连上并起扫描：
        // 模块 Open 时可能就要登记自己的数据块。它不是模块，不在下面的模块列表里，自己就是一棵扫描树的根。
        // 这儿按组件类型取而不是走 PlcComponent.Current——Current 是给上层读写用的 IPlc，不带装配这一面。
        foreach (var plc in roots.OfType<PlcComponent>())
        {
            if (!plc.Open())
            {
                LogHelper.Error(plc.Name, "PLC 连接失败");
            }

            plc.Start();
        }

        // IO 表：读点表。排在 PLC 之后、模块之前，供上层按索引取点；值直接从 PLC 缓存解出来，不另起采集。
        foreach (var io in roots.OfType<IoComponent>())
        {
            io.Open();
        }

        // 安全信号（Safety 节点下的急停、维修门、漏液、厂务气源/排风这些）：不是模块，给它单起一条扫描线程监控。
        // 排在 IO 表之后：子节点读点要走点表。
        foreach (var safety in roots.OfType<SafetyComponent>())
        {
            safety.Start();
        }

        var modules = roots.OfType<BaseModule>().Where(m => m.IsEnabled).ToList();

        // 各轴把收发两块登记进 PLC 缓存，与 Init（回零）分开；断线重连由轴自身扫描处理。
        var axisPlc = PlcComponent.Current;
        if (axisPlc is not null)
        {
            var outputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var axis in roots.OfType<AxisComponent>()
                         .Concat(roots.SelectMany(root => root.FindChildren<AxisComponent>())).Distinct())
            {
                if (!string.IsNullOrWhiteSpace(axis.SendPlcDataPath) && !outputPaths.Add(axis.SendPlcDataPath))
                {
                    throw new InvalidOperationException($"轴命令块路径重复: {axis.SendPlcDataPath}");
                }

                if (!axis.Open(axisPlc))
                {
                    LogHelper.Warn(axis.FullPath, "轴收发块未配置，未登记 PLC 数据块");
                }
            }
        }

        // 先连接后启动：模块在此打开驱动连接。
        foreach (var module in modules)
        {
            if (!module.Open())
            {
                LogHelper.Error(module.Name, "驱动连接失败");
            }
        }

        foreach (var module in modules)
        {
            module.Start();
        }

        // 搬运管理：模块全起来之后再绑表启动——它一转就会执行搬运单，
        // 不能在模块还没连上驱动、还没 Home 的时候就开始派机械手。
        var transfers = TransferManager.Current;
        if (transfers is not null)
        {
            transfers.Bind(modules);
            transfers.Start();
        }
        else
        {
            LogHelper.Warn("Transfer", "sc.xml 没配 Transfer 节点：手动传片与自动派单都不可用");
        }

        // 流程配方库：可选站点按 sc.xml 的分组节点和装起来的模块生成（机械手站点表里有的才算），所以等模块全起来再绑。
        // 内容一变就通知客户端（只带编号），流程配方页收到后重拉。库在模块层（这边才认得 EventBus 和契约），桥搭在这儿。
        var sequences = SequenceComponent.Current;
        if (sequences is not null)
        {
            sequences.Bind(settings, modules);
            sequences.Changed += index =>
                EventBus.Send(new SequenceChangedDto { Index = index }, SequenceListDto.EventToken, retain: false);
        }
        else
        {
            LogHelper.Warn("Sequence", "sc.xml 没配 Sequence 节点：流程配方页用不了");
        }

        // 设备总状态（红 = 报警、黄 = 警告、绿 = 运行）：点亮四色灯并推给客户端顶栏。
        EquipmentStatusPublisher.Start(roots, modules);

        // IO 点位：按周期整包推给 IO 界面，界面只订阅不拉。
        IoPublisher.Start();

        // 数据曲线：每秒采一整行入库——只记 sc.xml 里配置了的（组件上的 SV + 组件绑的 IO）；
        // 实时曲线订阅同一份采样，留最近一段、推给界面。
        // 放在最后：采样要读 SV 编号表和 IO 点表，都得先备好。
        var dataChart = DataChartComponent.Current;
        if (dataChart is not null)
        {
            RealChartComponent.Current?.Attach(dataChart);
            RealChartPublisher.Start();
            dataChart.StartSampling(roots);
        }
        else
        {
            LogHelper.Warn("DataChart", "sc.xml 没配 DataChart 节点：数据曲线、实时曲线都没有数据");
        }

        LogHelper.Info($"组件装配 {roots.Count} 个，启动模块 {modules.Count} 个：{string.Join(", ", modules.Select(m => m.Name))}");

        #endregion

        #region gRPC 服务注册

        RoleMappingConfig.Register();

        services.AddTransient<IRoleService, RoleService>();
        services.AddTransient<IUserService, UserService>();
        services.AddTransient<ILoadPortService, LoadPortService>();
        services.AddTransient<IRobotService, RobotService>();
        services.AddTransient<IChamberService, ChamberService>();
        services.AddTransient<IAlarmService, AlarmService>();
        services.AddTransient<ISystemService, SystemService>();
        services.AddTransient<IIoService, IoService>();
        services.AddTransient<IEcService, EcService>();
        services.AddTransient<IWaferLedgerService, WaferLedgerService>();
        services.AddTransient<ISequenceService, SequenceService>();
        services.AddTransient<IDataChartService, DataChartService>();
        services.AddTransient<IRealChartService, RealChartService>();

        #endregion

        return services;
    }
}
