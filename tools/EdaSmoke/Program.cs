using System.Collections.Concurrent;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Xml;
using CoreWCF.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using xyz.Elements;
using xyz.Elements.Attributes;
using xyz.Elements.Collectors;
using xyz.Elements.Elements;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Elements.Models;
using xyz.Configs.Models;
using xyz.Database.DbProvider;
using F2 = xyz.Secs.Eda.Freeze2;

// EDA 冒烟（Interface A，Freeze 2）：Eda 节点照 machine.xml 同款装配，旁边挂一个带 SV / EC / DV / 事件 / 报警的探针元件，
// 再挂一个 Eap 节点（链路启用、不接设备）让自描述带上 E164 的 GEM300 部分。假的厂里客户端（FDC）用 SEMI WSDL 生成的客户端调设备，
// 自己用 CoreWCF 起一个收数据的口（会话问活、采集报告、计划通知都发到这一个地址）。
// 验证：接口发现、建会话 / 问活 / 关会话 / 会话数上限；权限表（空着全放、加了照条目、不认识的权限）；设备自描述（结构树、节点描述、类型、
// 状态机、SEMI 对象、报警、单位）；读参数当前值；采集计划（毛病回报、重号、定义、查、激活、定周期采样、事件、报警、停用、删除、在跑不能删）；
// 开始停止条件（参数条件）；E164 标准事件（PJ 转换按号对、同号按状态认去向、E30 控制状态）；持久计划会话断了休眠、同一客户端再连上接着跑、存库；
// 设备定时问活；宿主退出给客户端发"会话关了"。
SmokeIoc.Reset();

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("FAIL: " + message);
    }

    checks++;
}

const string Equipment = "SmokeTool";
const string ClientId = "FDC1";
const string ProbeNode = Equipment + "/Chamber/Chamber1";
const string JobManager = Equipment + "/JobManager";
const string CommonNamespace = "urn:semi-org:xsd.E138.V0709.ccs";

var directory = Path.Combine(Path.GetTempPath(), "EdaSmoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
XyzDb.Register("EdaSmoke", $"DataSource={Path.Combine(directory, "eda.db")}", SqlSugar.DbType.Sqlite);
int equipmentPort = 8800 + Random.Shared.Next(300);
int clientPort = equipmentPort + 300;

// ── 装配 ──────────────────────────────────────────────────────────────────
var alarmNode = new ModuleConfig
{
    Name = "Alarm",
    Type = typeof(AlarmElement).FullName!,
    Values = { new ValueConfig { Name = "EnableHistory", Value = "False" } },
};
var chamberNode = new ModuleConfig
{
    Name = "Chamber",
    Children = { new ModuleConfig { Name = "Chamber1", Type = typeof(EdaProbe).FullName! } },
};
var eapNode = new ModuleConfig
{
    Name = "Eap",
    Type = typeof(EapElement).FullName!,
    Children =
    {
        new ModuleConfig
        {
            Name = "Hsms",
            Type = typeof(HsmsElement).FullName!,
            Values =
            {
                new ValueConfig { Name = "IsEnable", Value = "True" },
                new ValueConfig { Name = "Port", Value = (equipmentPort + 600).ToString() },
            },
        },
        new ModuleConfig { Name = "Notifier", Type = typeof(EapNotifierElement).FullName! },
        new ModuleConfig { Name = "E30", Type = typeof(E30Element).FullName!, Values = { new ValueConfig { Name = "Database", Value = "EdaSmoke" } } },
        new ModuleConfig { Name = "E39", Type = typeof(E39Element).FullName! },
        new ModuleConfig { Name = "Recipe", Type = typeof(E30RecipeElement).FullName! },
        new ModuleConfig { Name = "E87", Type = typeof(E87Element).FullName! },
        new ModuleConfig { Name = "E90", Type = typeof(E90Element).FullName! },
        new ModuleConfig { Name = "E40", Type = typeof(E40Element).FullName! },
        new ModuleConfig { Name = "E94", Type = typeof(E94Element).FullName! },
    },
};
var edaNode = new ModuleConfig
{
    Name = "Eda",
    Type = typeof(EdaElement).FullName!,
    Values =
    {
        new ValueConfig { Name = "IsEnable", Value = "True" },
        new ValueConfig { Name = "Host", Value = "127.0.0.1" },
        new ValueConfig { Name = "Port", Value = equipmentPort.ToString() },
        new ValueConfig { Name = "EquipmentName", Value = Equipment },
        new ValueConfig { Name = "Database", Value = "EdaSmoke" },
        new ValueConfig { Name = "DefaultMaxSessions", Value = "5" },
    },
};

var roots = SmokeIoc.Load([alarmNode, chamberNode, eapNode, edaNode]);
var ec = new EcElement();
SmokeIoc.Use<IEc>(ec);
ec.Merge(roots);
var collectors = new GemCollectors();
collectors.Merge(roots, directory);
SmokeIoc.Use(collectors);

var eda = roots.OfType<EdaElement>().Single();
var probe = roots.OfType<EdaProbe>().Single();
var eap = roots.OfType<EapElement>().Single();
var e30 = eap.FindChild<E30Element>()!;
var e40 = eap.FindChild<E40Element>()!;
probe.Temperature = 36.5;

var client = new FakeClient();
var clientApp = FakeClient.Start(client, clientPort);
string clientUrl = $"http://127.0.0.1:{clientPort}/Consumer";

eda.Bind();
eda.OnStarted();
Check(eda.IsListening, "EDA 起来了、在监听");
Check(eda.DiscoveryAddress == $"http://127.0.0.1:{equipmentPort}/E132InterfaceDiscovery", "接口发现地址按 Host、Port 拼");

var binding = new CustomBinding(
    new TextMessageEncodingBindingElement(MessageVersion.Soap12, Encoding.UTF8),
    new HttpTransportBindingElement { MaxReceivedMessageSize = 64 * 1024 * 1024 });
T Open<T>(string url)
{
    return new ChannelFactory<T>(binding, new EndpointAddress(url)).CreateChannel();
}

F2.SyncHeader Header(string session)
{
    return new F2.SyncHeader { From = ClientId, To = Equipment, SessionId = session };
}

// ── 1. 接口发现 ────────────────────────────────────────────────────────────
var discovery = Open<F2.InterfaceDiscovery>(eda.DiscoveryAddress);
var interfaces = (await discovery.GetInterfacesAsync(new F2.GetInterfacesRequestIn { GetInterfacesRequest = new F2.GetInterfacesRequestType() }))
    .GetInterfacesResponse.EquipmentInterfaceDescription.Single();
var group = interfaces.SessionGroup.Single();
Check(interfaces.Name == Equipment, "接口发现回设备名");
Check(group.SessionManager.URL == $"http://127.0.0.1:{equipmentPort}/E132/SessionManager", "会话管理的地址按客户端连进来的主机和端口回");
Check(group.InterfaceDescription.Select(item => item.Standard).SequenceEqual(["E125", "E134"]), "另外两个接口是 E125 自描述、E134 采集");
var sessionManager = Open<F2.SessionManager>(group.SessionManager.URL);
var security = Open<F2.SecurityAdmin>(group.SecurityAdmin.URL);
var metadataService = Open<F2.EquipmentMetadataManager>(group.InterfaceDescription[0].URL);
var data = Open<F2.DataCollectionManager>(group.InterfaceDescription[1].URL);

// ── 2. 会话 ───────────────────────────────────────────────────────────────
async Task<string> Establish()
{
    var reply = await sessionManager.EstablishSessionAsync(new F2.EstablishSessionRequestIn
    {
        SyncHeader = Header(string.Empty),
        EstablishSessionRequest = new F2.EstablishSessionRequestType
        {
            EndPoint = new F2.EndPointSelectorType { Item = new F2.HTTPEndPointType { URL = clientUrl } },
        },
    });
    Check(reply.EstablishSessionResponse.Item is string, "建会话回会话号");
    Check(reply.SyncHeader.From == Equipment && reply.SyncHeader.To == ClientId, "回复的消息头 From 是设备、To 是客户端");
    return (string)reply.EstablishSessionResponse.Item;
}

string session = await Establish();
var ping = await sessionManager.SessionPingAsync(new F2.SessionPingRequestIn1 { SyncHeader = Header(session), SessionPingRequest = new F2.SessionPingRequestType() });
Check(ping.SessionPingResponse.Item as string == Equipment, "客户端问活回设备 ID");
var bogus = await metadataService.GetUnitsAsync(new F2.GetUnitsIn { SyncHeader = Header("bogus"), GetUnitsRequest = new F2.GetUnitsRequestType() });
Check(bogus.GetUnitsResponse.Item is F2.ErrorType bogusError && bogusError.code == 1 && bogusError.source == Equipment, "没建过的会话号回错误 1（会话没建立）");

// ── 3. 设备自描述 ─────────────────────────────────────────────────────────
var structure = (F2.EquipmentType)(await metadataService.GetEquipmentStructureAsync(new F2.GetEquipmentStructureIn
{
    SyncHeader = Header(session),
    GetEquipmentStructureRequest = new F2.GetEquipmentStructureRequestType(),
})).GetEquipmentStructureResponse.Item;
Check(structure.Name == Equipment, "结构树的根是设备名");
Check(structure.LogicalElements.Select(element => element.Name).SequenceEqual(["JobManager", "MaterialManager"]), "EAP 链路启用时带 E164 的 JobManager、MaterialManager 两个逻辑节点");
var chamber = structure.EquipmentComponents.Subsystems.Single(item => item.Name == "Chamber");
Check(chamber.ElementType == "Group" && chamber.SubsystemComponents.IODevices is null, "machine.xml 的分组也成节点（Group）");
Check(chamber.SubsystemComponents.Subsystems.Single().Name == "Chamber1", "探针元件挂在分组下面");
Check(structure.EquipmentComponents.Subsystems.Any(item => item.Name == "Eda"), "EDA 自己的元件也在树上");

var descriptions = (F2.NodeDescriptionResultType)(await metadataService.GetEquipmentNodeDescriptionsAsync(new F2.GetEquipmentNodeDescriptionsIn
{
    SyncHeader = Header(session),
    GetEquipmentNodeDescriptionsRequest = [ProbeNode, Equipment + "/Nope"],
})).GetEquipmentNodeDescriptionsResponse.Item;
var probeDescription = descriptions.NodeDescription.Single();
Check(descriptions.InvalidEquipmentNodeId.Single() == Equipment + "/Nope", "认不出的节点放进 InvalidEquipmentNodeId");
var temperature = probeDescription.Parameter.Single(parameter => parameter.parameterName == "Temperature");
Check(temperature.typeName == "double" && temperature.Item is F2.DataType && ((F2.DataType)temperature.Item).isTransient == F2.isTransientEnum.Unrestricted, "SV 是随时能读的数据（double）");
Check((string)temperature.Units.Item == "degC" && temperature.SECSVariable.Single().SECSVarClass == F2.SECSVarEnumType.SV
    && temperature.SECSVariable.Single().SECSVID == collectors.Sv.Definitions.Single(row => row.Name == "Chamber.Chamber1.Temperature").Id, "SV 带单位和 SVID");
var gain = probeDescription.Parameter.Single(parameter => parameter.parameterName == "Gain");
Check(gain.Item is F2.ConfigurationType && gain.SECSVariable.Single().SECSVarClass == F2.SECSVarEnumType.ECV, "EC 是配置、带 ECID");
var stepData = probeDescription.Parameter.Single(parameter => parameter.parameterName == "StepNo");
Check(((F2.DataType)stepData.Item).isTransient == F2.isTransientEnum.Transient && stepData.SECSVariable.Single().SECSVarClass == F2.SECSVarEnumType.DV, "DV 只在事件那一刻有");
var stepEvent = probeDescription.SimpleEvent.Single(item => item.eventId == "StepStarted");
Check(stepEvent.SECSCEID == collectors.Event.CeidOf("Chamber.Chamber1.StepStarted").ToString()
    && stepEvent.AvailableParameter.Single().parameterName == "StepNo" && stepEvent.AvailableParameter.Single().source is null, "事件带 CEID 和它的 DV");
var overTemp = probeDescription.ExceptionRef.Single();
Check(overTemp.exceptionId == "Chamber.Chamber1.OverTemp" && overTemp.SECSALID is not null, "报警带 ALID");

var equipmentDescription = ((F2.NodeDescriptionResultType)(await metadataService.GetEquipmentNodeDescriptionsAsync(new F2.GetEquipmentNodeDescriptionsIn
{
    SyncHeader = Header(session),
    GetEquipmentNodeDescriptionsRequest = [Equipment, JobManager],
})).GetEquipmentNodeDescriptionsResponse.Item).NodeDescription;
var control = equipmentDescription[0].StateMachineInstance.Single();
Check(control.stateMachineId == "E30-0307:Control" && control.EventMap.Select(map => map.eventId).SequenceEqual(["Control:OffLine", "Control:Local", "Control:Remote"]),
    "整机节点上有 E30 控制状态机实例，三个事件对上我们 E30 报的");
Check(control.EventMap.All(map => map.SECSCEID is not null), "控制状态事件填了我们的 CEID");
var controlState = equipmentDescription[0].Parameter.Single(parameter => parameter.parameterName == "ControlState");
Check(controlState.typeName == "ControlStateEnum" && controlState.SECSVariable.Single().SECSVID == collectors.Sv.Definitions.Single(row => row.Name == "Eap.E30.ControlState").Id,
    "整机上的标准参数 ControlState 读的是我们 E30 的 SV");
Check(equipmentDescription[1].ObjTypeRef.Select(reference => reference.objType).SequenceEqual(["ProcessJob", "ControlJob"]), "JobManager 管 PJ、CJ");

var types = (F2.ParameterTypesType)(await metadataService.GetTypeDefinitionsAsync(new F2.GetTypeDefinitionsIn
{
    SyncHeader = Header(session),
    GetTypeDefinitionsRequest = new F2.GetTypeDefinitionsRequestType(),
})).GetTypeDefinitionsResponse.Item;
Check(types.TypeDefinition.Any(type => type.typeName == "double") && types.TypeDefinition.Any(type => type.typeName == "PRJobStateEnum" && type.Item is F2.EnumerationType),
    "类型定义有公共的、也有 E164 带的枚举");
var machines = (await metadataService.GetStateMachinesAsync(new F2.GetStateMachinesIn
{
    SyncHeader = Header(session),
    GetStateMachinesRequest = new F2.GetStateMachinesRequestType(),
})).GetStateMachinesResponse.Items.Cast<F2.StateMachineType>().ToList();
Check(machines.Any(machine => machine.stateMachineId == "E40-0709:ProcessJob") && machines.Any(machine => machine.stateMachineId == "E30-0307:Control"), "状态机有 E40、E30 的");
var objTypes = (await metadataService.GetSEMIObjTypesAsync(new F2.GetSEMIObjTypesIn
{
    SyncHeader = Header(session),
    GetSEMIObjTypesRequest = new F2.GetSEMIObjTypesRequestType(),
})).GetSEMIObjTypesResponse.Items.Cast<F2.SEMIObjType>().ToList();
var processJob = objTypes.Single(type => type.objType == "ProcessJob");
var pjMaps = processJob.StateMachineInstance.EventMap;
Check(pjMaps.Any(map => map.eventId == "ProcessJob:5:WaitingForStart-Processing" && map.SECSCEID == collectors.Event.CeidOf("Eap.E40.PrJobSMTrans05").ToString()),
    "PJ 的转换事件填了我们 E40 报的 CEID");
Check(processJob.SimpleEvent is null, "样例里我们不报的普通事件去掉了");
Check(objTypes.Select(type => type.objType).OrderBy(name => name).SequenceEqual(["Carrier", "ControlJob", "ProcessJob", "SubstLoc", "Substrate"]), "SEMI 对象是 PJ、CJ、载具、片、片位");
var exceptions = (await metadataService.GetExceptionsAsync(new F2.GetExceptionsIn { SyncHeader = Header(session), GetExceptionsRequest = new F2.GetExceptionsRequestType() }))
    .GetExceptionsResponse.Items.Cast<F2.ExceptionType>().ToList();
Check(exceptions.Any(item => item.exceptionId == "Chamber.Chamber1.OverTemp" && item.severity == F2.severityEnumType.Error && item.exceptionName == "温度超了"), "报警定义带文字和轻重");
var units = (F2.UnitSetType)(await metadataService.GetUnitsAsync(new F2.GetUnitsIn { SyncHeader = Header(session), GetUnitsRequest = new F2.GetUnitsRequestType() }))
    .GetUnitsResponse.Item;
Check(units.Unit.Any(unit => unit.symbol == "degC"), "单位里有参数用到的");

// ── 4. 读参数 ─────────────────────────────────────────────────────────────
var values = (F2.PVSetType)(await data.GetParameterValuesAsync(new F2.GetParameterValuesIn
{
    SyncHeader = Header(session),
    GetParameterValuesRequest =
    [
        Parameter(ProbeNode, "Temperature"),
        Parameter(ProbeNode, "Gain"),
        Parameter(ProbeNode, "StepNo"),
        Parameter(ProbeNode, "Nope"),
        Parameter(Equipment + "/Nope", "Temperature"),
    ],
})).GetParameterValuesResponse.Item;
Check(values.ItemsElementName[0] == F2.ItemsChoiceType1.F8 && (double)values.Items[0] == 36.5, "SV 读当前值");
Check(values.ItemsElementName[1] == F2.ItemsChoiceType1.F8 && (double)values.Items[1] == 1.5, "EC 读当前值");
Check(NoValue(values, 2) == F2.NoValueReasonEnumType.ValueNotAvailable, "DV 平时读不到");
Check(NoValue(values, 3) == F2.NoValueReasonEnumType.NoSuchParameter && NoValue(values, 4) == F2.NoValueReasonEnumType.NoSuchSource, "没有的参数、节点回原因");

// ── 5. 采集计划 ───────────────────────────────────────────────────────────
var invalid = await Define(new F2.DataCollectionPlanType
{
    id = "BAD",
    name = "bad",
    Description = "bad plan",
    EventRequest = [new F2.EventRequestType { sourceId = Equipment + "/Nope", eventId = "StepStarted" }],
    TraceRequest =
    [
        new F2.TraceRequestType { id = 1, intervalInSeconds = 0.01f, collectionCount = 3, groupSize = 5, ParameterRequest = [Parameter(ProbeNode, "StepNo")] },
    ],
});
var invalidPlan = (F2.InvalidPlanType)((F2.DefinePlanErrorType)invalid).Item;
Check(((F2.DefinePlanErrorType)invalid).Error.code == 7 && invalidPlan.planId == "BAD", "计划不对回错误 7 和毛病");
Check(invalidPlan.InvalidEvent.Single().invalidSourceId, "事件请求的节点不对");
var invalidTrace = invalidPlan.InvalidTraceRequest.Single();
Check(Math.Abs(invalidTrace.InvalidInterval.validInterval - 0.1f) < 0.0001 && invalidTrace.InvalidGroupSizeForInterval.validGroupSize == 3, "周期太短回能做的最短周期，几次一组比总次数多回能用的值");
Check(invalidTrace.InvalidParameter.Single().invalidContext, "定周期采样要了只在事件那一刻有的 DV");

var defined = await Define(new F2.DataCollectionPlanType
{
    id = "P1",
    name = "plan one",
    Description = "event + exception + trace",
    EventRequest = [new F2.EventRequestType { sourceId = ProbeNode, eventId = "StepStarted", ParameterRequest = [Parameter(ProbeNode, "StepNo"), Parameter(ProbeNode, "Temperature")] }],
    ExceptionRequest = [new F2.ExceptionRequestType()],
    TraceRequest = [new F2.TraceRequestType { id = 1, intervalInSeconds = 0.1f, collectionCount = 3, groupSize = 3, ParameterRequest = [Parameter(ProbeNode, "Temperature")] }],
});
Check(defined is F2.DCPDefinedType definedPlan && definedPlan.planId == "P1" && definedPlan.definedBy == ClientId, "计划定义进来了，记下谁定义的");
var duplicate = await Define(new F2.DataCollectionPlanType { id = "P1", name = "again", Description = string.Empty });
Check(((F2.InvalidPlanType)((F2.DefinePlanErrorType)duplicate).Item).DuplicatePlanId.planId == "P1", "重号回占着的那份");
var definedIds = (await data.GetDefinedPlanIdsAsync(new F2.GetDefinedPlanIdsIn { SyncHeader = Header(session), GetDefinedPlanIdsRequest = new F2.GetDefinedPlanIdsRequestType() }))
    .GetDefinedPlanIdsResponse.Items.Cast<F2.DCPDefinedType>().ToList();
Check(definedIds.Select(item => item.planId).SequenceEqual(["P1"]), "列出定义了的计划");
var definition = (F2.DataCollectionPlanType)(await data.GetPlanDefinitionAsync(new F2.GetPlanDefinitionIn
{
    SyncHeader = Header(session),
    GetPlanDefinitionRequest = new F2.GetPlanDefinitionRequestType { PlanId = "P1" },
})).GetPlanDefinitionResponse.Item;
Check(definition.EventRequest.Single().ParameterRequest.Length == 2 && definition.TraceRequest.Single().groupSize == 3 && definition.ExceptionRequest.Single().sourceId is null,
    "取回的计划内容跟定义的一样");

var activated = await Activate("P1");
Check(activated is F2.DCPActivatedType activatedPlan && activatedPlan.activatedBy == ClientId, "激活了，记下谁激活的");
var traceReport = client.Take<F2.TraceReportType>("P1", report => report.traceId == 1);
Check(traceReport.TR.Select(sample => sample.i).SequenceEqual([1, 2, 3]) && traceReport.TR.All(sample => (double)sample.PVSet.Items[0] == 36.5),
    "定周期采样采够 3 次一组发一条，每次的值都在");
Check(traceReport.StartTrigger is null && !traceReport.startTriggerTimeSpecified, "没有开始条件的一激活就开始，报告里不带开始条件");

probe.Step("S1");
var eventReport = client.Take<F2.EventReportType>("P1", report => report.eventId == "StepStarted");
Check(eventReport.sourceId == ProbeNode && (string)eventReport.PVSet.Items[0] == "S1" && (double)eventReport.PVSet.Items[1] == 36.5,
    "事件报告带事件那一刻的 DV 和 SV 当前值");
probe.Overheat();
var exceptionReport = client.Take<F2.ExceptionReportType>("P1", report => report.exceptionId == "Chamber.Chamber1.OverTemp");
Check(exceptionReport.state == "Set" && exceptionReport.severity == "Error" && exceptionReport.sourceId == ProbeNode && exceptionReport.description == "温度超了",
    "报警报出了出报警报告");

var deleteActive = (F2.DeletePlanErrorType)(await data.DeletePlanAsync(new F2.DeletePlanIn
{
    SyncHeader = Header(session),
    DeletePlanRequest = new F2.DeletePlanRequestType { planId = "P1" },
})).DeletePlanResponse.Item;
Check(deleteActive.Error.code == 5 && deleteActive.Item is F2.DCPIsActiveType, "在跑的计划不能删");
var reactivate = (F2.ActivatePlanErrorType)await Activate("P1");
Check(reactivate.Error.code == 5 && reactivate.Item is F2.DCPActivatedType, "在跑的再激活回错误带激活情况");
var active = (await data.GetActivePlanIdsAsync(new F2.GetActivePlanIdsIn { SyncHeader = Header(session), GetActivePlanIdsRequest = new F2.GetActivePlanIdsRequestType() }))
    .GetActivePlanIdsResponse.Items;
Check(active.Cast<F2.DCPActivatedType>().Single().planId == "P1", "列出在跑的计划");
var deactivated = (await data.DeactivatePlanAsync(new F2.DeactivatePlanIn
{
    SyncHeader = Header(session),
    DeactivatePlanRequest = new F2.DeactivatePlanRequestType { planId = "P1", terminate = true },
})).DeactivatePlanResponse.Item;
Check(deactivated is F2.DCPDeactivatedType stopped && stopped.deactivatedBy == ClientId, "停用了");
var notActive = (F2.DeactivatePlanErrorType)(await data.DeactivatePlanAsync(new F2.DeactivatePlanIn
{
    SyncHeader = Header(session),
    DeactivatePlanRequest = new F2.DeactivatePlanRequestType { planId = "P1" },
})).DeactivatePlanResponse.Item;
Check(notActive.Error.code == 6 && notActive.Item is F2.DCPNotActiveType, "没在跑的停用回错误 6");
var deleted = (await data.DeletePlanAsync(new F2.DeletePlanIn { SyncHeader = Header(session), DeletePlanRequest = new F2.DeletePlanRequestType { planId = "P1" } }))
    .DeletePlanResponse.Item;
Check(deleted is F2.DCPDeletedType, "停了的计划能删");
var noPlan = (F2.GetPlanDefinitionErrorType)(await data.GetPlanDefinitionAsync(new F2.GetPlanDefinitionIn
{
    SyncHeader = Header(session),
    GetPlanDefinitionRequest = new F2.GetPlanDefinitionRequestType { PlanId = "P1" },
})).GetPlanDefinitionResponse.Item;
Check(noPlan.Error.code == 4 && noPlan.Item is F2.NoSuchPlanType, "删了的计划查不到");

// ── 6. 开始 / 停止条件 ────────────────────────────────────────────────────
F2.EventTriggerType StepIs(string value)
{
    var document = new XmlDocument();
    var element = document.CreateElement("S", CommonNamespace);
    element.InnerText = value;
    return new F2.EventTriggerType
    {
        sourceId = ProbeNode,
        eventId = "StepStarted",
        Condition =
        [
            new F2.ConditionType
            {
                Operator = F2.OperatorType.EQ,
                ConditionValue = element,
                Parameter = new F2.ParameterReferenceType1 { SourceId = ProbeNode, ParameterName = "StepNo" },
            },
        ],
    };
}

await Define(new F2.DataCollectionPlanType
{
    id = "P2",
    name = "triggered",
    Description = "start on GO, stop on END",
    TraceRequest =
    [
        new F2.TraceRequestType
        {
            id = 7,
            intervalInSeconds = 0.1f,
            collectionCount = 0,
            groupSize = 1000,
            StartOn = [StepIs("GO")],
            StopOn = [StepIs("END")],
            ParameterRequest = [Parameter(ProbeNode, "Temperature")],
        },
    ],
});
await Activate("P2");
probe.Step("X");
Thread.Sleep(300);
Check(!client.Has<F2.TraceReportType>("P2"), "条件不成立不开始");
probe.Step("GO");
Thread.Sleep(450);
probe.Step("END");
var triggered = client.Take<F2.TraceReportType>("P2", report => report.traceId == 7);
Check(triggered.TR.Length >= 3 && triggered.startTriggerTimeSpecified && triggered.stopTriggerTimeSpecified, "条件成立开始、停止条件停，不满一组的也发");
Check(((F2.EventTriggerType)triggered.StartTrigger.Single()).eventId == "StepStarted" && ((F2.EventTriggerType)triggered.StopTrigger.Single()).eventId == "StepStarted",
    "报告带开始、停止条件");
await Deactivate("P2");

// ── 7. E164 标准事件 ──────────────────────────────────────────────────────
await Define(new F2.DataCollectionPlanType
{
    id = "P3",
    name = "gem300",
    Description = "E164 events",
    EventRequest =
    [
        new F2.EventRequestType
        {
            sourceId = JobManager,
            eventId = "ProcessJob:5:WaitingForStart-Processing",
            ParameterRequest = [Parameter(JobManager, "ObjID"), Parameter(JobManager, "ProcessJobCurrState"), Parameter(JobManager, "PRJobState")],
        },
        new F2.EventRequestType { sourceId = JobManager, eventId = "ProcessJob:10:Pause-WaitingForStart", ParameterRequest = [Parameter(JobManager, "ObjID")] },
        new F2.EventRequestType
        {
            sourceId = Equipment,
            eventId = "Control:Remote",
            ParameterRequest = [Parameter(Equipment, "ControlCurrState"), Parameter(Equipment, "ControlState")],
        },
    ],
});
await Activate("P3");
eda.Report(e40, "PrJobSMTrans05", new GemData("PRJobID", "PJ-1"), new GemData("PRJobState", (byte)3));
var pjStart = client.Take<F2.EventReportType>("P3", report => report.eventId == "ProcessJob:5:WaitingForStart-Processing");
Check(pjStart.sourceId == JobManager && (string)pjStart.PVSet.Items[0] == "PJ-1" && (string)pjStart.PVSet.Items[1] == "Processing"
    && Convert.ToInt32(pjStart.PVSet.Items[2]) == 3, "PJ 转换 #5 翻成 E164 事件，带对象号、当前状态名、事件里的状态");
eda.Report(e40, "PrJobSMTrans10", new GemData("PRJobID", "PJ-2"), new GemData("PRJobState", (byte)2));
var pjResume = client.Take<F2.EventReportType>("P3", report => report.eventId.StartsWith("ProcessJob:10:"));
Check(pjResume.eventId == "ProcessJob:10:Pause-WaitingForStart" && (string)pjResume.PVSet.Items[0] == "PJ-2", "同一转换号有几个去向的按事件里的状态认");
eda.Report(e30, "ControlStateRemote");
var remote = client.Take<F2.EventReportType>("P3", report => report.eventId == "Control:Remote");
Check(remote.sourceId == Equipment && (string)remote.PVSet.Items[0] == "Remote" && remote.PVSet.ItemsElementName[1] == F2.ItemsChoiceType1.I2,
    "E30 控制状态事件挂在整机上，带状态名和 ControlState 当前值");
await Deactivate("P3");

// ── 8. 权限 ───────────────────────────────────────────────────────────────
var unknownPrivilege = (await security.AddACLEntryAsync(AclEntry(ClientId, "E999.Nope"))).AddACLEntryResponse.Error;
Check(unknownPrivilege.Error.code == 9 && unknownPrivilege.Item is F2.UnrecognizedPrivilegeType, "不认识的权限回错误 9、不加");
var added = (await security.AddACLEntryAsync(AclEntry(ClientId, "E132.AddACLEntry", "E132.DeleteACLEntry", "E132.GetACL"))).AddACLEntryResponse.Error;
Check(added is null, "权限表加了一条");
var denied = (await metadataService.GetUnitsAsync(new F2.GetUnitsIn { SyncHeader = Header(session), GetUnitsRequest = new F2.GetUnitsRequestType() })).GetUnitsResponse.Item;
Check(denied is F2.ErrorType deniedError && deniedError.code == 2, "权限表不空就照条目管：没给的权限回错误 2");
var deniedPlan = (F2.GetDefinedPlanIdsErrorType)(await data.GetDefinedPlanIdsAsync(new F2.GetDefinedPlanIdsIn
{
    SyncHeader = Header(session),
    GetDefinedPlanIdsRequest = new F2.GetDefinedPlanIdsRequestType(),
})).GetDefinedPlanIdsResponse.Items.Single();
Check(deniedPlan.Item.RequiredPrivilege.Single().PrivilegeId == "E134.GetDefinedPlanIds", "缺权限的错误带要哪项权限");
var acl = (await security.GetACLAsync(new F2.GetACLRequestIn { SyncHeader = Header(session), GetACLRequest = new F2.GetACLRequestType() })).GetACLResponse.Items;
Check(((F2.PrivilegeAssignmentType)((F2.ACLEntrySelectorType)acl.Single()).Item).Privilege.Length == 3, "权限表能查");
await security.DeleteACLEntryAsync(new F2.DeleteACLEntryRequestIn
{
    SyncHeader = Header(session),
    DeleteACLEntryRequest = new F2.DeleteACLEntryRequestType { SubjectID = ClientId },
});
var allowed = (await metadataService.GetUnitsAsync(new F2.GetUnitsIn { SyncHeader = Header(session), GetUnitsRequest = new F2.GetUnitsRequestType() })).GetUnitsResponse.Item;
Check(allowed is F2.UnitSetType, "删光了权限表又全放");

// ── 9. 会话数上限、持久计划休眠 ────────────────────────────────────────────
await security.SetMaxSessionsAsync(new F2.SetMaxSessionsRequestIn { SyncHeader = Header(session), SetMaxSessionsRequest = new F2.SetMaxSessionsRequestType { MaxSessions = 1 } });
var full = (await sessionManager.EstablishSessionAsync(new F2.EstablishSessionRequestIn
{
    SyncHeader = Header(string.Empty),
    EstablishSessionRequest = new F2.EstablishSessionRequestType { EndPoint = new F2.EndPointSelectorType { Item = new F2.HTTPEndPointType { URL = clientUrl } } },
})).EstablishSessionResponse.Item;
Check(full is F2.EstablishSessionErrorType fullError && fullError.Error.code == 3, "会话数满了回错误 3");
await security.SetMaxSessionsAsync(new F2.SetMaxSessionsRequestIn { SyncHeader = Header(session), SetMaxSessionsRequest = new F2.SetMaxSessionsRequestType { MaxSessions = 5 } });

await Define(new F2.DataCollectionPlanType
{
    id = "P4",
    name = "persistent",
    Description = "survives the session",
    isPersistent = true,
    EventRequest = [new F2.EventRequestType { sourceId = ProbeNode, eventId = "StepStarted", ParameterRequest = [Parameter(ProbeNode, "StepNo")] }],
});
await Activate("P4");
await sessionManager.CloseSessionAsync(new F2.CloseSessionRequestIn { SyncHeader = Header(session), CloseSessionRequest = new F2.CloseSessionRequestType { SessionId = session } });
var closed = (await metadataService.GetUnitsAsync(new F2.GetUnitsIn { SyncHeader = Header(session), GetUnitsRequest = new F2.GetUnitsRequestType() })).GetUnitsResponse.Item;
Check(closed is F2.ErrorType closedError && closedError.code == 1, "关了的会话不能再用");
using (var db = XyzDb.Create("EdaSmoke"))
{
    var row = db.Queryable<xyz.Database.Eda.EdaConfigEntity>().InSingle(2);
    Check(row is not null && row.Json.Contains("\"P4\"") && row.Json.Contains("\"wasActive\":true"), "持久计划存库，记着在跑");
}

session = await Establish();
var resumed = (await data.GetActivePlanIdsAsync(new F2.GetActivePlanIdsIn { SyncHeader = Header(session), GetActivePlanIdsRequest = new F2.GetActivePlanIdsRequestType() }))
    .GetActivePlanIdsResponse.Items.Cast<F2.DCPActivatedType>().ToList();
Check(resumed.Single().planId == "P4", "持久计划会话断了休眠，同一客户端再连上接着跑");
var remaining = (await data.GetDefinedPlanIdsAsync(new F2.GetDefinedPlanIdsIn { SyncHeader = Header(session), GetDefinedPlanIdsRequest = new F2.GetDefinedPlanIdsRequestType() }))
    .GetDefinedPlanIdsResponse.Items.Cast<F2.DCPDefinedType>().Select(item => item.planId).ToList();
Check(remaining.SequenceEqual(["P4"]), "不持久的计划跟着定义它的客户端走了就删掉");
probe.Step("S2");
var afterResume = client.Take<F2.EventReportType>("P4", report => report.eventId == "StepStarted");
Check((string)afterResume.PVSet.Items[0] == "S2", "接着跑的计划采到的发给新会话");

// ── 10. 杂项、问活、退出 ──────────────────────────────────────────────────
var now = (await data.GetCurrentDateTimeAsync(new F2.GetCurrentDateTimeInput { SyncHeader = Header(session), GetCurrentDateTimeRequest = new F2.GetCurrentDateTimeRequestType() }))
    .GetCurrentDateTimeResponse.Item;
Check(now is DateTime clock && Math.Abs((clock - DateTime.Now).TotalSeconds) < 5, "对时回设备时钟");
var performance = (F2.PerformanceStatusType)(await data.GetCurrentPerformanceStatusAsync(new F2.GetCurrentPerformanceStatusIn
{
    SyncHeader = Header(session),
    GetCurrentPerformanceStatusRequest = new F2.GetCurrentPerformanceStatusRequestType(),
})).GetCurrentPerformanceStatusResponse.Item;
Check(!performance.isBelowThreshold && performance.ActivePlan.Single().planId == "P4", "跟得上，列出在跑的计划");
var sessions = (await security.GetActiveSessionsAsync(new F2.GetActiveSessionsRequestIn { SyncHeader = Header(session), GetActiveSessionsRequest = new F2.GetActiveSessionsRequestType() }))
    .GetActiveSessionsResponse.Items.Cast<F2.ActiveSessionType>().ToList();
Check(sessions.Single().ClientID == ClientId && ((F2.HTTPEndPointType)sessions.Single().SessionEndPoint.Item).URL == clientUrl, "列出会话和回送地址");

eda.SessionPingIntervalSec = 1;
Check(SpinWait.SpinUntil(() => client.Pings > 0, 5000), "设备定时问客户端还在不在");
eda.OnStopping();
Check(SpinWait.SpinUntil(() => client.ClosedSessions.Contains(session), 5000), "宿主退出给还在的会话发会话关了");
Check(!eda.IsListening, "退出后不再监听");
await clientApp.StopAsync();

Console.WriteLine($"EdaSmoke OK: {checks} checks");
return;

// ── 帮手 ──────────────────────────────────────────────────────────────────
static F2.ParameterRequestType Parameter(string source, string name)
{
    return new F2.ParameterRequestType { sourceId = source, parameterName = name };
}

static F2.NoValueReasonEnumType? NoValue(F2.PVSetType set, int index)
{
    if (set.ItemsElementName[index] != F2.ItemsChoiceType1.NoValue)
    {
        return null;
    }

    return ((F2.NoValueType)set.Items[index]).reasonCode;
}

F2.AddACLEntryRequestIn AclEntry(string subject, params string[] privileges)
{
    return new F2.AddACLEntryRequestIn
    {
        SyncHeader = Header(session),
        AddACLEntryRequest = new F2.AddACLEntryRequestType
        {
            ACLEntry = new F2.ACLEntrySelectorType
            {
                Item = new F2.PrivilegeAssignmentType
                {
                    Subject = new F2.SubjectSelectorType { Item = new F2.PrincipalType { ID = subject } },
                    Privilege = privileges.Select(privilege => new F2.PrivilegeType { PrivilegeId = privilege }).ToArray(),
                },
            },
        },
    };
}

async Task<object> Define(F2.DataCollectionPlanType plan)
{
    var reply = await data.DefinePlanAsync(new F2.DefinePlanIn { SyncHeader = Header(session), DefinePlanRequest = new F2.DefinePlanRequestType { NewPlan = plan } });
    return reply.DefinePlanResponse.Item;
}

async Task<object> Activate(string planId)
{
    var reply = await data.ActivatePlanAsync(new F2.ActivatePlanIn { SyncHeader = Header(session), ActivatePlanRequest = new F2.ActivatePlanRequestType { PlanId = planId } });
    return reply.ActivatePlanResponse.Item;
}

async Task Deactivate(string planId)
{
    var reply = await data.DeactivatePlanAsync(new F2.DeactivatePlanIn
    {
        SyncHeader = Header(session),
        DeactivatePlanRequest = new F2.DeactivatePlanRequestType { planId = planId, terminate = true },
    });
    Check(reply.DeactivatePlanResponse.Item is F2.DCPDeactivatedType, $"计划 {planId} 停用了");
}

/// <summary>
/// 冒烟探针：一个温度 SV（单位 degC）、一个增益 EC、一个步骤号 DV、一个"步骤开始"事件（带步骤号）、一个温度超了报警
/// </summary>
[Element(description: "EdaSmoke 探针")]
public sealed class EdaProbe : ElementBase
{
    [VariableMark(VariableType.SV, ValueFormat.Double, "degC", description: "温度")]
    public double Temperature { get; set; }

    [VariableMark(VariableType.EC, ValueFormat.Double, "", "0", "10", "1.5", "增益")]
    public double Gain
    {
        get { return GetEcDouble(nameof(Gain)); }
    }

    [DataVariable(ValueFormat.String, "步骤号")]
    public readonly string StepData = "StepNo";

    [EventAttribut("步骤开始", Data = new[] { "StepNo" })]
    public readonly string StepStarted = "StepStarted";

    [Alarm("温度超了", AlarmCategory.ParameterControlError)]
    public string OverTemp = "OverTemp";

    /// <summary>
    /// 报"步骤开始"
    /// </summary>
    public void Step(string step)
    {
        RaiseEvent(StepStarted, new GemData(StepData, step));
    }

    /// <summary>
    /// 报"温度超了"
    /// </summary>
    public void Overheat()
    {
        RaiseAlarm(OverTemp);
    }
}

/// <summary>
/// 假的厂里客户端收数据的口：设备的问活、会话关了、采集报告、计划通知都发到这一个地址，收到的按先后记下
/// </summary>
[CoreWCF.ServiceBehavior(InstanceContextMode = CoreWCF.InstanceContextMode.Single, ConcurrencyMode = CoreWCF.ConcurrencyMode.Multiple)]
public sealed class FakeClient : F2.SessionClient, F2.DCPConsumer
{
    private readonly ConcurrentQueue<F2.DataCollectionReportType> _reports = new();
    private int _pings;

    /// <summary>
    /// 收到几次问活
    /// </summary>
    public int Pings => Volatile.Read(ref _pings);

    /// <summary>
    /// 收到"会话关了"的会话号
    /// </summary>
    public ConcurrentBag<string> ClosedSessions { get; } = [];

    /// <summary>
    /// 起收数据的口（CoreWCF，SOAP 1.2，跟设备一样）
    /// </summary>
    public static WebApplication Start(FakeClient client, int port)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(System.Net.IPAddress.Loopback, port, listen =>
            {
                listen.Protocols = HttpProtocols.Http1;
            });
        });
        builder.Services.AddServiceModelServices();
        builder.Services.AddSingleton(client);
        var app = builder.Build();
        ((IApplicationBuilder)app).UseServiceModel(serviceBuilder =>
        {
            var serverBinding = new CoreWCF.Channels.CustomBinding(
                new CoreWCF.Channels.TextMessageEncodingBindingElement(CoreWCF.Channels.MessageVersion.Soap12, Encoding.UTF8),
                new CoreWCF.Channels.HttpTransportBindingElement { MaxReceivedMessageSize = 64 * 1024 * 1024 });
            serviceBuilder.AddService<FakeClient>();
            serviceBuilder.AddServiceEndpoint<FakeClient, F2.SessionClient>(serverBinding, "/Consumer");
            serviceBuilder.AddServiceEndpoint<FakeClient, F2.DCPConsumer>(serverBinding, "/Consumer");
        });
        app.StartAsync().GetAwaiter().GetResult();
        return app;
    }

    /// <summary>
    /// 等这份计划的一条报告（最多 5 秒），拿到就从记录里去掉
    /// </summary>
    public T Take<T>(string planId, Func<T, bool> match) where T : class
    {
        T? found = null;
        bool got = SpinWait.SpinUntil(() =>
        {
            found = _reports.Where(report => report.planID == planId)
                .SelectMany(report => report.Items)
                .OfType<T>()
                .FirstOrDefault(match);
            return found is not null;
        }, 5000);
        if (!got)
        {
            throw new InvalidOperationException($"FAIL: 5 秒内没等到计划 {planId} 的 {typeof(T).Name}");
        }

        return found!;
    }

    /// <summary>
    /// 这份计划有没有收到过这种报告
    /// </summary>
    public bool Has<T>(string planId)
    {
        return _reports.Where(report => report.planID == planId).SelectMany(report => report.Items).OfType<T>().Any();
    }

    public Task<F2.SessionPingResponseOut> SessionPingAsync(F2.SessionPingRequestIn request)
    {
        Interlocked.Increment(ref _pings);
        return Task.FromResult(new F2.SessionPingResponseOut { SessionPingResponse = new F2.SessionPingResponseType { Item = "FDC1" } });
    }

    public Task SessionFrozenAsync(F2.SessionFrozenRequestIn request)
    {
        return Task.CompletedTask;
    }

    public Task SessionClosedAsync(F2.SessionClosedRequestIn request)
    {
        ClosedSessions.Add(request.SessionClosedNotification.SessionId);
        return Task.CompletedTask;
    }

    public Task NewDataAsync(F2.NewDataIn request)
    {
        _reports.Enqueue(request.NewDataNotification.DCR);
        return Task.CompletedTask;
    }

    public Task PerformanceWarningAsync(F2.PerformanceWarningIn request)
    {
        return Task.CompletedTask;
    }

    public Task PerformanceRestoredAsync(F2.PerformanceRestoredIn request)
    {
        return Task.CompletedTask;
    }

    public Task DCPDeactivationAsync(F2.DCPDeactivationIn request)
    {
        return Task.CompletedTask;
    }

    public Task DCPHibernationAsync(F2.DCPHibernationIn request)
    {
        return Task.CompletedTask;
    }

    public Task DCPDefinedAsync(F2.DCPDefinedIn request)
    {
        return Task.CompletedTask;
    }

    public Task DCPDeletedAsync(F2.DCPDeletedIn request)
    {
        return Task.CompletedTask;
    }
}
