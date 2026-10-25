using xyz.Components.Collectors;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Secs.Gem;
using xyz.Secs.Gem.E30;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// xyz.Secs 的 E30 要的编号表和当前值：编号查 GEM 编号表（GemCollectors），SV、EC 现读组件，Host 改 EC 写回 ec.xml（IEc），
/// 正在报着的报警问报警组件。编号表、EC、报警组件没装的当没有
/// </summary>
internal sealed class GemDictionary : IGemDictionary
{
    /// <summary>
    /// Host 改 EC（S2F15）的那一刻：EC 变化的通知也在这条线程上发，E30 组件见到这个标记就不当成操作员改的
    /// </summary>
    [ThreadStatic]
    private static bool _applyingHostConstants;

    private readonly GemCollectors? _collectors;
    private readonly IEc? _ec;
    private readonly IAlarm? _alarms;

    public GemDictionary(GemCollectors? collectors, IEc? ec, IAlarm? alarms)
    {
        _collectors = collectors;
        _ec = ec;
        _alarms = alarms;
    }

    /// <summary>
    /// 这条线程正在替 Host 改 EC
    /// </summary>
    public static bool ApplyingHostConstants => _applyingHostConstants;

    public IReadOnlyList<GemVariableInfo> StatusVariables
    {
        get
        {
            if (_collectors is null)
            {
                return Array.Empty<GemVariableInfo>();
            }

            return _collectors.Sv.Collect().Where(sv => sv.Visible).Select(sv => new GemVariableInfo((uint)sv.Svid, sv.Name, sv.Unit)).ToList();
        }
    }

    public IReadOnlyList<GemVariableInfo> DataVariables
    {
        get
        {
            if (_collectors is null)
            {
                return Array.Empty<GemVariableInfo>();
            }

            return _collectors.Dv.Collect().Select(dv => new GemVariableInfo((uint)dv.Dvid, dv.Name, dv.Unit)).ToList();
        }
    }

    public IReadOnlyList<GemConstantInfo> Constants
    {
        get
        {
            if (_collectors is null)
            {
                return Array.Empty<GemConstantInfo>();
            }

            return _collectors.Ec.Collect().Where(ec => ec.Visible)
                .Select(ec => new GemConstantInfo((uint)ec.Ecid, ec.Name, ec.Unit, ec.Min, ec.Max, ec.Default, ec.Format)).ToList();
        }
    }

    public IReadOnlyList<GemEventInfo> Events
    {
        get
        {
            if (_collectors is null)
            {
                return Array.Empty<GemEventInfo>();
            }

            return _collectors.Event.Collect()
                .Select(item => new GemEventInfo((uint)item.Ceid, item.Name, item.Dvids.Select(dvid => (uint)dvid).ToList())).ToList();
        }
    }

    public IReadOnlyList<GemAlarmInfo> Alarms
    {
        get
        {
            if (_collectors is null)
            {
                return Array.Empty<GemAlarmInfo>();
            }

            return _collectors.Alarm.Definitions.Where(row => row.Enabled)
                .Select(row => new GemAlarmInfo((uint)row.Id, row.Name, CategoryOf(row))).ToList();
        }
    }

    public IReadOnlyList<uint> ActiveAlarms
    {
        get
        {
            if (_collectors is null || _alarms is null)
            {
                return Array.Empty<uint>();
            }

            var active = _alarms.ActiveAlarms.Select(alarm => $"{alarm.SourcePath}.{alarm.AlarmCode}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _collectors.Alarm.Definitions.Where(row => row.Enabled && active.Contains(row.Name)).Select(row => (uint)row.Id).ToList();
        }
    }

    public SecsItem? ReadStatus(uint svid)
    {
        if (_collectors is null || svid > int.MaxValue)
        {
            return null;
        }

        if (!_collectors.Sv.TryRead((int)svid, out var row, out object? raw) || row is null || !row.Visible)
        {
            return null;
        }

        return GemValue.From(raw, row.Format);
    }

    public SecsItem? ReadConstant(uint ecid)
    {
        if (_collectors is null || ecid > int.MaxValue)
        {
            return null;
        }

        var ec = _collectors.Ec.ByEcid((int)ecid);
        if (ec is null || !ec.Visible)
        {
            return null;
        }

        return GemValue.FromText(ec.Value, ec.Format);
    }

    public SecsItem? EmptyData(uint dvid)
    {
        if (_collectors is null || dvid > int.MaxValue)
        {
            return null;
        }

        var dv = _collectors.Dv.ByDvid((int)dvid);
        if (dv is null)
        {
            return null;
        }

        return GemValue.Empty(dv.Format);
    }

    public GemConstantCheck CheckConstant(uint ecid, SecsItem value)
    {
        if (!TryLocate(ecid, value, out string path, out string name, out string? text))
        {
            return GemConstantCheck.NotFound;
        }

        if (text is null || _ec is null)
        {
            return GemConstantCheck.Invalid;
        }

        var check = _ec.Check(path, name, text);
        if (check == EcSetResult.NotFound)
        {
            return GemConstantCheck.NotFound;
        }

        if (check != EcSetResult.Ok)
        {
            return GemConstantCheck.Invalid;
        }

        return GemConstantCheck.Ok;
    }

    public bool SetConstant(uint ecid, SecsItem value)
    {
        if (_ec is null || !TryLocate(ecid, value, out string path, out string name, out string? text) || text is null)
        {
            return false;
        }

        _applyingHostConstants = true;
        try
        {
            return _ec.TrySet(path, name, text, out _) != EcSetResult.SaveFailed;
        }
        finally
        {
            _applyingHostConstants = false;
        }
    }

    /// <summary>
    /// 找一个在用、标了上传的 EC：path / name 是它在 ec.xml 里的位置，text 是 Host 给的值按 EC 的格式转成的写法（格式对不上为 null）
    /// </summary>
    private bool TryLocate(uint ecid, SecsItem value, out string path, out string name, out string? text)
    {
        path = string.Empty;
        name = string.Empty;
        text = null;
        if (_collectors is null || _ec is null || ecid > int.MaxValue || !_collectors.Ec.TryLocate((int)ecid, out path, out name))
        {
            return false;
        }

        var row = _collectors.Ec.ByEcid((int)ecid);
        if (row is null || !row.Visible)
        {
            return false;
        }

        text = GemValue.ToEcText(value, row.Format);
        return true;
    }

    /// <summary>
    /// 编号表里记的报警分类名换回 E5 的分类数（ALCD 低 7 位）；名字认不出当"不用"（0）
    /// </summary>
    private static byte CategoryOf(AlarmDefinition definition)
    {
        if (Enum.TryParse(definition.Category, out AlarmCategory category))
        {
            return (byte)category;
        }

        return (byte)AlarmCategory.NotUsed;
    }
}
