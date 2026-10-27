using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using xyz.Common.Log;
using xyz.Elements.Attributes;
using xyz.Elements.Collectors;
using xyz.Elements.Interfaces;
using xyz.Secs.Eda.E164;
using xyz.Secs.Eda.Model;

namespace xyz.Elements.Elements;

/// <summary>
/// 开机按元件树和 GEM 编号表建设备自描述（E125），不另配：
/// 结构照 machine.xml 的树（分组也成节点；模块和下面挂着模块的分组算 Module，没子节点的设备元件算 IODevice，别的算 Subsystem）；
/// 参数是 SV / EC / DV（看 Visible 的照样看），事件、报警照编号表挂到各自元件的节点上；
/// EAP 链路启用时再按 E164 带上 GEM300 的状态机、对象类型、JobManager / MaterialManager 两个逻辑节点和整机上的 E30 控制状态。
/// 同时记下每个能随时读的参数怎么读
/// </summary>
internal sealed class EdaMetadataBuilder
{
    private const string GroupType = "Group";

    private static readonly Regex NamePattern = new("^[a-zA-Z]([a-zA-Z\\-_. 0-9]*[a-zA-Z\\-_.0-9])?$");

    private readonly string _equipmentName;
    private readonly IReadOnlyList<ElementBase> _roots;
    private readonly GemCollectors? _collectors;
    private readonly EdaE164Sources _e164;
    private readonly ILogSource _owner;
    private readonly Dictionary<string, EdaNode> _byPath = new(StringComparer.Ordinal);
    private readonly Dictionary<EdaNode, ElementBase> _elements = [];

    public EdaMetadataBuilder(string equipmentName, IReadOnlyList<ElementBase> roots, GemCollectors? collectors, EdaE164Sources e164, ILogSource owner)
    {
        _equipmentName = equipmentName;
        _roots = roots;
        _collectors = collectors;
        _e164 = e164;
        _owner = owner;
    }

    /// <summary>
    /// 能随时读的参数怎么读（键见 ReaderKey）；只在事件那一刻有的 DV 不在里面
    /// </summary>
    public Dictionary<string, Func<EdaValue>> Readers { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 元件全路径 → 节点路径
    /// </summary>
    public Dictionary<string, string> Locators { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Readers 的键：节点路径 + 参数名
    /// </summary>
    public static string ReaderKey(string locator, string name)
    {
        return locator + "\n" + name;
    }

    /// <summary>
    /// 建
    /// </summary>
    public EdaMetadata Build()
    {
        var equipment = new EdaNode
        {
            Name = SafeName(_equipmentName),
            Locator = SafeName(_equipmentName),
            Kind = EdaNodeKind.Equipment,
            ElementType = "Equipment",
            Description = "整台设备",
            Uid = Uid(SafeName(_equipmentName)),
        };
        AddTree(_roots, equipment);
        DecideKind(equipment);
        AddManagers(equipment);

        var metadata = new EdaMetadata(equipment, DateTime.Now);
        metadata.Types.AddRange(E164Templates.TypeDefinitions("CommonGetTypeDefinitions.xml"));
        if (_collectors is not null)
        {
            AddVariables(metadata, _collectors);
            AddEvents(_collectors);
            AddAlarms(metadata, _collectors);
            AddE164(metadata, _collectors);
        }

        AddUnits(metadata);
        LogHelper.Info(_owner, $"设备自描述建好：{metadata.Nodes.Count} 个节点，{metadata.Nodes.Sum(node => node.Parameters.Count)} 个参数，"
            + $"{metadata.Nodes.Sum(node => node.Events.Count)} 个事件，{metadata.Exceptions.Count} 个报警，{metadata.ObjTypes.Count} 种 SEMI 对象");
        return metadata;
    }

    #region 结构

    private void AddTree(IEnumerable<ElementBase> elements, EdaNode equipment)
    {
        foreach (var element in elements)
        {
            if (!element.IsEnabled)
            {
                continue;
            }

            var node = NodeFor(element.FullPath, equipment, element);
            _elements[node] = element;
            Locators[element.FullPath] = node.Locator;
            AddTree(element.Children, equipment);
        }
    }

    /// <summary>
    /// 按全路径找节点，没有就建（路径里的分组也建成节点）；先父后子走树，元件自己总比它的子元件先建
    /// </summary>
    private EdaNode NodeFor(string fullPath, EdaNode equipment, ElementBase? element)
    {
        if (_byPath.TryGetValue(fullPath, out var existing))
        {
            return existing;
        }

        var parent = equipment;
        string name = fullPath;
        int dot = fullPath.LastIndexOf('.');
        if (dot > 0)
        {
            parent = NodeFor(fullPath[..dot], equipment, null);
            name = fullPath[(dot + 1)..];
        }

        string safe = SafeName(name);
        if (parent.Children.Any(child => child.Name == safe))
        {
            safe = $"{safe}_{parent.Children.Count + 1}";
            LogHelper.Warn(_owner, $"{fullPath} 跟同一层的节点重名，EDA 里叫 {safe}");
        }

        string locator = parent.Locator + "/" + safe;
        var node = new EdaNode
        {
            Name = safe,
            Locator = locator,
            Kind = EdaNodeKind.Subsystem,
            ElementType = element?.GetType().Name ?? GroupType,
            Description = element?.GetType().GetCustomAttribute<ElementAttribute>()?.Description ?? string.Empty,
            Uid = Uid(locator),
        };
        parent.Children.Add(node);
        _byPath[fullPath] = node;
        return node;
    }

    /// <summary>
    /// 定节点种类（先子后父）：模块、下面挂着模块的都算模块；没子节点的设备元件算末端器件；别的算子系统。返回下面有没有模块
    /// </summary>
    private bool DecideKind(EdaNode node)
    {
        bool containsModule = false;
        foreach (var child in node.Children)
        {
            if (DecideKind(child))
            {
                containsModule = true;
            }
        }

        if (node.Kind == EdaNodeKind.Equipment)
        {
            return containsModule;
        }

        _elements.TryGetValue(node, out var element);
        if (element is IModule || containsModule)
        {
            node.Kind = EdaNodeKind.Module;
            return true;
        }

        if (node.Children.Count == 0 && element is IDevice)
        {
            node.Kind = EdaNodeKind.IoDevice;
            return false;
        }

        node.Kind = EdaNodeKind.Subsystem;
        return false;
    }

    /// <summary>
    /// E164 的两个逻辑节点：有 PJ / CJ 的加 JobManager，有载具 / 片的加 MaterialManager
    /// </summary>
    private void AddManagers(EdaNode equipment)
    {
        if (_e164.E40 is not null || _e164.E94 is not null)
        {
            equipment.Children.Add(Manager(equipment, E164Object.JobManager, "管 PJ、CJ（E40、E94）"));
        }

        if (_e164.E87 is not null || _e164.E90 is not null)
        {
            equipment.Children.Add(Manager(equipment, E164Object.MaterialManager, "管载具、片、片位（E87、E90）"));
        }
    }

    private static EdaNode Manager(EdaNode equipment, string name, string description)
    {
        string locator = equipment.Locator + "/" + name;
        return new EdaNode
        {
            Name = name,
            Locator = locator,
            Kind = EdaNodeKind.LogicalElement,
            ElementType = name,
            Description = description,
            Uid = Uid(locator),
        };
    }

    #endregion

    #region 参数、事件、报警

    private void AddVariables(EdaMetadata metadata, GemCollectors collectors)
    {
        foreach (var row in collectors.Sv.Definitions.Where(row => row.Enabled && row.Visible))
        {
            if (!TryOwner(row.Name, out var node, out string local))
            {
                continue;
            }

            int svid = row.Id;
            AddParameter(node, new EdaParameter
            {
                Name = local,
                TypeName = TypeName(row.Format),
                Kind = EdaParameterKind.Data,
                Transience = EdaTransience.Unrestricted,
                Description = row.Description,
                Unit = row.Unit,
                SecsId = svid,
                SecsClass = EdaSecsClass.Sv,
            }, () => ReadSv(collectors, svid));
        }

        foreach (var row in collectors.Ec.Definitions.Where(row => row.Enabled && row.Visible))
        {
            if (!TryOwner(row.Name, out var node, out string local))
            {
                continue;
            }

            int ecid = row.Id;
            string format = row.Format;
            AddParameter(node, new EdaParameter
            {
                Name = local,
                TypeName = TypeName(row.Format),
                Kind = EdaParameterKind.Configuration,
                Transience = EdaTransience.Unrestricted,
                Description = row.Description,
                Unit = row.Unit,
                SecsId = ecid,
                SecsClass = EdaSecsClass.Ecv,
            }, () => ReadEc(collectors, ecid, format));
        }

        foreach (var row in collectors.Dv.Definitions.Where(row => row.Enabled))
        {
            if (!TryOwner(row.Name, out var node, out string local))
            {
                continue;
            }

            AddParameter(node, new EdaParameter
            {
                Name = local,
                TypeName = TypeName(row.Format),
                Kind = EdaParameterKind.Data,
                Transience = EdaTransience.Transient,
                Description = row.Description,
                Unit = row.Unit,
                SecsId = row.Id,
                SecsClass = EdaSecsClass.Dv,
            }, null);
        }
    }

    private void AddParameter(EdaNode node, EdaParameter parameter, Func<EdaValue>? reader)
    {
        if (node.Parameters.Any(existing => existing.Name == parameter.Name))
        {
            LogHelper.Warn(_owner, $"{node.Locator} 上的参数 {parameter.Name} 重名（SV / EC / DV 撞了），后面的不放进 EDA");
            return;
        }

        node.Parameters.Add(parameter);
        if (reader is not null)
        {
            Readers[ReaderKey(node.Locator, parameter.Name)] = reader;
        }
    }

    private void AddEvents(GemCollectors collectors)
    {
        foreach (var row in collectors.Event.Definitions.Where(row => row.Enabled))
        {
            if (!TryOwner(row.Name, out var node, out string local))
            {
                continue;
            }

            var parameters = new List<EdaParameterRef>();
            foreach (var payload in row.Payloads)
            {
                var dv = collectors.Dv.ByDvid(payload.Dvid);
                if (dv is null || !TryOwner(dv.Name, out var dvNode, out string dvLocal))
                {
                    continue;
                }

                string source = string.Empty;
                if (dvNode != node)
                {
                    source = dvNode.Locator;
                }

                parameters.Add(new EdaParameterRef { Source = source, Name = dvLocal });
            }

            string name = row.EventText;
            if (name.Length == 0)
            {
                name = local;
            }

            node.Events.Add(new EdaEvent { Id = local, Name = name, Description = row.Description, Ceid = row.Id, Parameters = parameters });
        }
    }

    private void AddAlarms(EdaMetadata metadata, GemCollectors collectors)
    {
        foreach (var row in collectors.Alarm.Definitions.Where(row => row.Enabled))
        {
            if (!TryOwner(row.Name, out var node, out _))
            {
                continue;
            }

            metadata.Exceptions.Add(new EdaException
            {
                Id = row.Name,
                Name = row.AlarmText,
                Severity = Severity(row.AlarmLevel),
                Description = row.Description,
            });
            node.Exceptions.Add(new EdaExceptionRef { ExceptionId = row.Name, Alid = row.Id, SetCeid = row.SetEventId, ClearCeid = row.ClearEventId });
        }
    }

    /// <summary>
    /// 编号表里的全名（元件全路径.代码）拆成节点和本地名；元件不在树上（停用、报警事件这类）的返回 false
    /// </summary>
    private bool TryOwner(string fullName, out EdaNode node, out string local)
    {
        node = null!;
        local = string.Empty;
        int dot = fullName.LastIndexOf('.');
        if (dot <= 0)
        {
            return false;
        }

        if (!_byPath.TryGetValue(fullName[..dot], out var found) || !_elements.ContainsKey(found))
        {
            return false;
        }

        node = found;
        local = fullName[(dot + 1)..];
        return true;
    }

    #endregion

    #region E164

    private void AddE164(EdaMetadata metadata, GemCollectors collectors)
    {
        var standards = new List<string>();
        if (_e164.E30 is not null)
        {
            standards.Add("E30");
        }

        var objects = new List<(E164Object Source, ElementBase Element)>();
        if (_e164.E40 is not null)
        {
            standards.Add("E40");
            objects.Add((E164Object.ProcessJob, _e164.E40));
        }

        if (_e164.E94 is not null)
        {
            standards.Add("E94");
            objects.Add((E164Object.ControlJob, _e164.E94));
        }

        if (_e164.E87 is not null)
        {
            standards.Add("E87");
            objects.Add((E164Object.Carrier, _e164.E87));
        }

        if (_e164.E90 is not null)
        {
            standards.Add("E90");
            objects.Add((E164Object.Substrate, _e164.E90));
            objects.Add((E164Object.SubstrateLocation, _e164.E90));
        }

        foreach (string standard in standards)
        {
            foreach (var type in E164Templates.TypeDefinitions($"{standard}GetTypeDefinitions.xml"))
            {
                if (metadata.Types.All(existing => existing.Name != type.Name))
                {
                    metadata.Types.Add(type);
                }
            }

            foreach (var machine in E164Templates.StateMachines($"{standard}GetStateMachines.xml"))
            {
                if (metadata.StateMachines.All(existing => existing.Id != machine.Id))
                {
                    metadata.StateMachines.Add(machine);
                }
            }
        }

        foreach (var (source, element) in objects)
        {
            var template = E164Templates.ObjTypes($"{source.Template}GetSEMIObjTypes.xml").FirstOrDefault(type => type.ObjType == source.ObjType);
            if (template is null)
            {
                LogHelper.Warn(_owner, $"E164 样例里没有对象类型 {source.ObjType}，EDA 不带它");
                continue;
            }

            metadata.ObjTypes.Add(Produced(template, source, element, collectors));
            var manager = metadata.Find(metadata.Equipment.Locator + "/" + source.Manager);
            manager?.ObjTypes.Add(new EdaObjTypeRef { ObjType = source.ObjType, StandardVersion = source.StandardVersion });
        }

        if (_e164.E30 is not null)
        {
            AddControlState(metadata.Equipment, _e164.E30, _e164.Recipe, collectors);
        }
    }

    /// <summary>
    /// 样例里的对象类型只留我们真报的转换事件（填上我们的 CEID）；样例里的普通事件我们不报，去掉
    /// </summary>
    private static EdaObjType Produced(EdaObjType template, E164Object source, ElementBase element, GemCollectors collectors)
    {
        return new EdaObjType
        {
            ObjType = template.ObjType,
            StandardVersion = template.StandardVersion,
            Attributes = template.Attributes,
            EventData = template.EventData,
            StateMachine = ProducedInstance(template.StateMachine, source, element, collectors),
        };
    }

    private static EdaStateMachineInstance? ProducedInstance(EdaStateMachineInstance? instance, E164Object source, ElementBase element, GemCollectors collectors)
    {
        if (instance is null)
        {
            return null;
        }

        var maps = new List<EdaEventMap>();
        foreach (var map in instance.Events)
        {
            if (!E164Templates.TrySplitEventId(map.EventId, out int number, out _, out _))
            {
                continue;
            }

            int ceid = collectors.Event.CeidOf($"{element.FullPath}.{source.CodePrefix}{number:00}");
            if (ceid == 0)
            {
                continue;
            }

            maps.Add(new EdaEventMap { EventId = map.EventId, Ceid = ceid, Parameters = map.Parameters });
        }

        return new EdaStateMachineInstance
        {
            StateMachineId = instance.StateMachineId,
            CurrentState = instance.CurrentState,
            PreviousState = instance.PreviousState,
            Events = maps,
            Nested = instance.Nested
                .Select(nested => ProducedInstance(nested, source, element, collectors))
                .Where(nested => nested is not null)
                .Select(nested => nested!)
                .ToList(),
        };
    }

    /// <summary>
    /// 整机节点上的 E30 部分：控制状态机实例（事件对上我们 E30 报的三个），几个标准参数直接读我们 E30 的 SV / EC，
    /// 配方变了、操作员改了 EC 两个标准事件
    /// </summary>
    private void AddControlState(EdaNode equipment, E30Element e30, E30RecipeElement? recipe, GemCollectors collectors)
    {
        if (!Locators.TryGetValue(e30.FullPath, out string? e30Locator))
        {
            return;
        }

        Alias(equipment, e30Locator, "ControlState", "ControlState", "ControlStateEnum");
        Alias(equipment, e30Locator, "Clock", "Clock", "String32");
        Alias(equipment, e30Locator, "EstablishCommunicationsTimeout", "EstablishCommunicationsTimeout", "int");
        Alias(equipment, e30Locator, "TimeFormat", "TimeFormat", "TimeFormatEnum");
        equipment.Parameters.Add(Transient(E164Events.ControlCurrState, "String128", "当前控制状态名（控制状态事件那一刻）"));
        equipment.Parameters.Add(Transient(E164Events.ControlPrevState, "String128", "上一个控制状态名（控制状态事件那一刻）"));

        var stateParameters = new List<EdaParameterRef>
        {
            new() { Name = E164Events.ControlCurrState },
            new() { Name = E164Events.ControlPrevState },
        };
        var maps = new List<EdaEventMap>();
        foreach (var control in E164Events.ControlEvents)
        {
            int ceid = collectors.Event.CeidOf($"{e30.FullPath}.{control.Code}");
            if (ceid > 0)
            {
                maps.Add(new EdaEventMap { EventId = control.EventId, Ceid = ceid, Parameters = stateParameters });
            }
        }

        equipment.StateMachines.Add(new EdaStateMachineInstance
        {
            StateMachineId = E164Events.ControlStateMachine,
            CurrentState = new EdaParameterRef { Name = E164Events.ControlCurrState },
            PreviousState = new EdaParameterRef { Name = E164Events.ControlPrevState },
            Events = maps,
        });

        int constantCeid = collectors.Event.CeidOf($"{e30.FullPath}.{E164Events.OperatorEquipmentConstantChange}");
        if (constantCeid > 0)
        {
            equipment.Events.Add(new EdaEvent
            {
                Id = E164Events.OperatorEquipmentConstantChange,
                Name = E164Events.OperatorEquipmentConstantChange,
                Description = "操作员在界面上改了 EC",
                Ceid = constantCeid,
            });
        }

        if (recipe is null)
        {
            return;
        }

        int recipeCeid = collectors.Event.CeidOf($"{recipe.FullPath}.{E164Events.ProcessProgramChange}");
        if (recipeCeid == 0)
        {
            return;
        }

        equipment.Parameters.Add(Transient("PPChangeName", "String255", "变了的配方名（配方变了那一刻）"));
        equipment.Parameters.Add(Transient("PPChangeStatus", "PPChangeStatusEnum", "怎么变的：1 新建、2 修改、3 删除"));
        equipment.Events.Add(new EdaEvent
        {
            Id = E164Events.ProcessProgramChange,
            Name = E164Events.ProcessProgramChange,
            Description = "流程配方或工艺配方新建、修改、删除了",
            Ceid = recipeCeid,
            Parameters = [new EdaParameterRef { Name = "PPChangeName" }, new EdaParameterRef { Name = "PPChangeStatus" }],
        });
    }

    /// <summary>
    /// 整机节点上放一个标准名字的参数，值、SECS 号都跟我们 E30 节点上那个一样
    /// </summary>
    private void Alias(EdaNode equipment, string e30Locator, string name, string ourName, string typeName)
    {
        if (!_byPath.TryGetValue(_e164.E30!.FullPath, out var e30Node))
        {
            return;
        }

        var ours = e30Node.Parameters.FirstOrDefault(parameter => parameter.Name == ourName);
        if (ours is null || !Readers.TryGetValue(ReaderKey(e30Locator, ourName), out var reader))
        {
            return;
        }

        AddParameter(equipment, new EdaParameter
        {
            Name = name,
            TypeName = typeName,
            Kind = ours.Kind,
            Transience = ours.Transience,
            Description = ours.Description,
            Unit = ours.Unit,
            SecsId = ours.SecsId,
            SecsClass = ours.SecsClass,
        }, reader);
    }

    private static EdaParameter Transient(string name, string typeName, string description)
    {
        return new EdaParameter
        {
            Name = name,
            TypeName = typeName,
            Kind = EdaParameterKind.Data,
            Transience = EdaTransience.Transient,
            Description = description,
        };
    }

    #endregion

    #region 单位、读值、换算

    private static void AddUnits(EdaMetadata metadata)
    {
        metadata.Units.AddRange(E164Templates.Units("CommonGetUnits.xml"));
        foreach (var node in metadata.Nodes)
        {
            foreach (var parameter in node.Parameters)
            {
                if (parameter.Unit.Length > 0 && metadata.Units.All(unit => unit.Symbol != parameter.Unit))
                {
                    metadata.Units.Add(new EdaUnit { Symbol = parameter.Unit, Name = parameter.Unit });
                }
            }
        }
    }

    private static EdaValue ReadSv(GemCollectors collectors, int svid)
    {
        if (!collectors.Sv.TryRead(svid, out _, out object? value))
        {
            return EdaValue.NoValue(EdaNoValueReason.NoSuchParameter, $"SVID {svid} is disabled");
        }

        return EdaValue.From(value);
    }

    /// <summary>
    /// EC 的值编号表里是文字，按格式转回数
    /// </summary>
    private static EdaValue ReadEc(GemCollectors collectors, int ecid, string format)
    {
        var ec = collectors.Ec.ByEcid(ecid);
        if (ec is null)
        {
            return EdaValue.NoValue(EdaNoValueReason.NoSuchParameter, $"ECID {ecid} is disabled");
        }

        switch (format)
        {
            case "Int":
            case "Enum":
                if (int.TryParse(ec.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
                {
                    return EdaValue.From(integer);
                }

                break;
            case "Double":
                if (double.TryParse(ec.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    return EdaValue.From(number);
                }

                break;
            case "Bool":
                if (bool.TryParse(ec.Value, out bool flag))
                {
                    return EdaValue.From(flag);
                }

                break;
        }

        return EdaValue.Text(ec.Value);
    }

    /// <summary>
    /// 编号表的格式 → E164 公共类型名
    /// </summary>
    private static string TypeName(string format)
    {
        switch (format)
        {
            case "Int":
            case "Enum":
                return "int";
            case "Double":
                return "double";
            case "Bool":
                return "boolean";
            default:
                return "string";
        }
    }

    private static EdaSeverity Severity(string level)
    {
        switch (level)
        {
            case "Fatal":
                return EdaSeverity.Fatal;
            case "Warn":
                return EdaSeverity.Warning;
            default:
                return EdaSeverity.Error;
        }
    }

    /// <summary>
    /// E120 的名字只能是字母开头，字母、数字、- _ . 空格；不合的字符换成 _，数字开头的前面补 N
    /// </summary>
    private static string SafeName(string name)
    {
        if (NamePattern.IsMatch(name))
        {
            return name;
        }

        var builder = new StringBuilder();
        foreach (char c in name)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('_');
            }
        }

        if (builder.Length == 0 || !char.IsAsciiLetter(builder[0]))
        {
            builder.Insert(0, 'N');
        }

        return builder.ToString();
    }

    /// <summary>
    /// 按路径算出来的唯一号：同一个路径每次开机都一样
    /// </summary>
    private static string Uid(string locator)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(locator));
        return new Guid(hash).ToString();
    }

    #endregion
}
