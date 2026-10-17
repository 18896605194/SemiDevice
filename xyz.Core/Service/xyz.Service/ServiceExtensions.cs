using Microsoft.Extensions.DependencyInjection;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Collectors;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Configs;
using xyz.Configs.Models;
using xyz.Modules;
using xyz.Service.Alarms;
using xyz.Service.Charts;
using xyz.Service.Events;
using xyz.Service.Jobs;
using xyz.Service.Recipes;
using xyz.Service.Systems;
using xyz.Service.Transfers;
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

        var roots = ComponentLoader.Load(settings);
        services.AddSingleton<IReadOnlyList<ComponentBase>>(roots);

        var ec = EcComponent.Current; //EC
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

        #region 采集EAP数据

        var collectors = new GemCollectors();
        collectors.Merge(roots, SC.ConfigDirectory);
        services.AddSingleton(collectors);

        #endregion

        #region 报警组件
        var alarms = AlarmComponent.Current;
        if (alarms is not null)
        {
            alarms.AlarmChanged += item => EventBus.Send(item.ToDto(), AlarmDto.EventToken, retain: false);
        }

        #endregion

        #region 晶圆账单管理

        var wafers = WaferManagerComponent.Current;
        if (wafers is not null)
        {
            void NotifyLedger(string module) =>
                EventBus.Send(new WaferLedgerChangedDto { Module = module }, WaferLedgerDto.EventToken, retain: false);

            wafers.WaferCreated += wafer => NotifyLedger(wafer.Module);
            wafers.WaferDeleted += wafer => NotifyLedger(wafer.Module);
            wafers.WaferUpdated += wafer => NotifyLedger(wafer.Module);
            wafers.WaferMoved += (wafer, _, _) => NotifyLedger(wafer.Module);
        }

        #endregion

        // 供液槽：有变化就推给界面
        TankPublisher.Bind(roots);

        //plc连接
        foreach (var plc in roots.OfType<PlcComponent>())
        {
            if (!plc.Open())
            {
                LogHelper.Error(plc.Name, "PLC 连接失败");
            }
        }

        // IO 点表先装载，扫描在全部初始化和依赖绑定完成后统一启动。
        foreach (var io in roots.OfType<IoComponent>())
        {
            io.Open();
        }

        var modules = roots.OfType<BaseModule>().Where(m => m.IsEnabled).ToList();

        // 各个轴手法登记进入 SendPlcDataPath、SendPlcDataPath
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

        //各模块的组件初始化：连驱动、登记晶圆账槽位这类不动硬件的开机准备，子组件由基类递归带着做；回原点不在这里
        foreach (var module in modules)
        {
            if (!module.InitComponent())
            {
                LogHelper.Error(module.Name, "组件初始化没全做成（驱动连接失败等，原因见前面的日志）");
            }
        }

        //晶圆账开机恢复
        wafers?.Restore();

        #region EAP 接设备（SECS/GEM，sc.xml 的 Eap 节点）：赶在模块开始扫描之前挂上上报口，链路等设备侧都起来以后再开

        // 开机时端口上已有的载具第一拍扫描就判到、自动读码，这时上报口没挂上，到达和读码结果就丢了：
        // E87 不知道这个载具，Host 没法认定，这一盒一直取不了片。所以先接设备，链路放到最后开（Host 连进来时处理方、设备都在）。
        var eap = EapComponent.Current;
        if (eap is not null)
        {
            eap.Bind(modules.OfType<ILoadPort>().ToList(), JobManager.Current, SequenceComponent.Current, RecipeComponent.Current);
        }
        else if (HsmsComponent.Current is not null)
        {
            LogHelper.Error("Eap", "sc.xml 的 Hsms 节点要放在 Eap 节点下（跟 E30 等标准组件一起），现在这样 EAP 不接");
        }

        #endregion

        // 整机（sc.xml 的 System 节点）：全部初始化、整机停止管的模块表，赶在它开始扫描之前绑上
        var system = SystemModule.Current;
        if (system is not null)
        {
            system.Bind(modules);
        }
        else
        {
            LogHelper.Warn("System", "sc.xml 没配 System 节点：没有系统状态，全部初始化、Auto / Manual、整机停止、全部回片都用不了");
        }

        #region 搬运管理

        // 先绑定模块表，扫描由后面的统一入口启动；挂在 Dispatch 下时跟随父组件扫描。
        var transfers = TransferManager.Current;
        if (transfers is not null)
        {
            transfers.Bind(modules);
        }
        else
        {
            LogHelper.Warn("Transfer", "sc.xml 没配 Transfer 节点：手动传片与自动派单都不可用");
        }
        #endregion

        #region 流程配方库

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

        #endregion

        #region 工艺配方库

        var processRecipes = RecipeComponent.Current;
        if (processRecipes is not null)
        {
            processRecipes.Bind(modules);
            processRecipes.Changed += index =>
                EventBus.Send(new ProcessRecipeChangedDto { Index = index }, ProcessRecipeListDto.EventToken, retain: false);

            // 字段的上限可以按晶圆尺寸分开写（Max.8、Max.12）：主界面切了尺寸，配方页要重拉字段表
            var waferSize = WaferSizeComponent.Current;
            if (waferSize is not null)
            {
                waferSize.Changed += _ =>
                    EventBus.Send(new ProcessRecipeChangedDto(), ProcessRecipeListDto.EventToken, retain: false);
            }
        }
        else
        {
            LogHelper.Warn("ProcessRecipe", "sc.xml 没配 ProcessRecipe 节点：工艺配方页用不了，流程配方、腔体起工艺不查配方在不在库里");
        }

        #endregion

        #region Job 管理

        // 跟搬运管理一样：挂在 Dispatch 下面的只绑表，扫描由 Dispatch 带着。
        var jobs = JobManager.Current;
        if (jobs is not null)
        {
            jobs.Bind(modules);
        }
        else
        {
            LogHelper.Warn("Job", "sc.xml 没配 Job 节点：建不了 Job，主界面的创建 / 启动 Job 用不了");
        }

        #endregion

        #region 曲线

        // 数据曲线先定信号表、开写库管道；采样在它自己的扫描里（sc 配 IsRun），跟下面的统一启动扫描一起起来
        var dataChart = DataChartComponent.Current;
        if (dataChart is not null)
        {
            RealChartComponent.Current?.Attach(dataChart);
            RealChartPublisher.Start();
            dataChart.Bind(roots);
        }
        else
        {
            LogHelper.Warn("DataChart", "sc.xml 没配 DataChart 节点：数据曲线、实时曲线都没有数据");
        }

        #endregion

        #region 统一启动扫描

        // 根节点通过 SC Value 的 IsRun 开启独立扫描；新增根组件无需在这里按类型补 Start。
        // 所有模块初始化、账本恢复和依赖绑定已完成；子组件由父组件递归扫描。
        ComponentLoader.StartScanning(roots);

        #endregion

        #region EAP 开链路：设备侧都起来以后再开

        eap?.Open();

        #endregion


        // 设备总状态（红 = 报警、黄 = 警告、绿 = 运行）：点亮四色灯并推给客户端顶栏。
        EquipmentStatusPublisher.Start(roots, modules);

        // IO 点位：按周期整包推给 IO 界面，界面只订阅不拉。
        IoPublisher.Start();

        var scanningRoots = roots.Where(root => root.IsEnabled && root.IsRun).ToList();
        LogHelper.Info($"根组件装配 {roots.Count} 个，模块 {modules.Count} 个，启动独立扫描 {scanningRoots.Count} 个："
            + string.Join(", ", scanningRoots.Select(root => root.FullPath)));

        #endregion

        #region gRPC 服务注册

        services.AddTransient<IRoleService, RoleService>();
        services.AddTransient<IUserService, UserService>();
        services.AddTransient<ILoadPortService, LoadPortService>();
        services.AddTransient<IRobotService, RobotService>();
        services.AddTransient<IChamberService, ChamberService>();
        services.AddTransient<IAlarmService, AlarmService>();
        services.AddTransient<ISystemService, SystemService>();
        services.AddTransient<IEquipmentService, EquipmentService>();
        services.AddTransient<IIoService, IoService>();
        services.AddTransient<IEcService, EcService>();
        services.AddTransient<IWaferLedgerService, WaferLedgerService>();
        services.AddTransient<ISequenceService, SequenceService>();
        services.AddTransient<IProcessRecipeService, ProcessRecipeService>();
        services.AddTransient<IDataChartService, DataChartService>();
        services.AddTransient<IRealChartService, RealChartService>();
        services.AddTransient<ITransferService, TransferService>();
        services.AddTransient<IJobService, JobService>();

        #endregion

        return services;
    }
}
