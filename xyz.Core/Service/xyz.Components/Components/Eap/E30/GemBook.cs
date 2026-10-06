using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// GEM 设定本：Host 定义的报告（S2F33）、事件挂哪些报告（S2F35）、事件和报警的开关（S2F37 / S5F3）、断通讯时缓存哪些报文（S2F43）。
/// 只管设定本身的读写和校验（照 E5 的应答码），不管变量的值、不管发报文。线程安全，改完由 E30 存盘。
/// 校验都是"要么全改、要么都不改"：一条报文里有一项不对，整条不生效。
/// </summary>
internal sealed class GemBook
{
    /// <summary>DRACK / LRACK / ERACK / ACKC5 共用的"收下了"。</summary>
    public const byte Accepted = 0;

    /// <summary>DRACK 3：报告号已经定义过。</summary>
    public const byte ReportAlreadyDefined = 3;

    /// <summary>DRACK 4：有变量号不存在。</summary>
    public const byte VariableNotExist = 4;

    /// <summary>LRACK 3：事件已经挂了报告。</summary>
    public const byte LinkAlreadyDefined = 3;

    /// <summary>LRACK 4：事件号不存在。</summary>
    public const byte EventNotExist = 4;

    /// <summary>LRACK 5：报告号不存在。</summary>
    public const byte ReportNotExist = 5;

    /// <summary>ERACK 1 / ACKC5 1：有编号不存在。</summary>
    public const byte Denied = 1;

    private readonly object _gate = new();
    private GemConfig _config = new();

    /// <summary>开机：接回存着的设定（没存过就是空的）。</summary>
    public void Load(GemConfig? config)
    {
        lock (_gate)
        {
            _config = config ?? new GemConfig();
        }
    }

    /// <summary>转成存盘用的 JSON；before 在锁里先把别处管的状态（缓存的）写进设定。</summary>
    public string Serialize(Action<GemConfig> before)
    {
        lock (_gate)
        {
            before(_config);
            return JsonHelper.Serialize(_config);
        }
    }

    /// <summary>存着的设定（开机给缓存接回状态用）。</summary>
    public GemConfig Config
    {
        get
        {
            lock (_gate)
            {
                return _config;
            }
        }
    }

    #region 报告（S2F33）

    /// <summary>
    /// S2F33 定义报告 → DRACK。一项都没有 = 删掉全部报告（链接跟着删）；某个报告的变量表为空 = 删掉这个报告（链接里也去掉）。
    /// 已经定义过的报告号不能再定义（先删再定义），变量号都要存在。
    /// </summary>
    public byte DefineReports(IReadOnlyList<(uint ReportId, IReadOnlyList<uint> Variables)> reports, Func<uint, bool> variableExists)
    {
        lock (_gate)
        {
            if (reports.Count == 0)
            {
                _config.Reports.Clear();
                _config.Links.Clear();
                return Accepted;
            }

            var seen = new HashSet<uint>();
            foreach (var (reportId, variables) in reports)
            {
                if (variables.Count == 0)
                {
                    continue;
                }

                if (_config.Reports.ContainsKey(reportId) || !seen.Add(reportId))
                {
                    return ReportAlreadyDefined;
                }

                if (variables.Any(variable => !variableExists(variable)))
                {
                    return VariableNotExist;
                }
            }

            foreach (var (reportId, variables) in reports)
            {
                if (variables.Count == 0)
                {
                    _config.Reports.Remove(reportId);
                    foreach (var link in _config.Links.Values)
                    {
                        link.RemoveAll(id => id == reportId);
                    }

                    continue;
                }

                _config.Reports[reportId] = variables.ToList();
            }

            return Accepted;
        }
    }

    /// <summary>一个报告带的变量；没定义返回 null。</summary>
    public IReadOnlyList<uint>? Report(uint reportId)
    {
        lock (_gate)
        {
            return _config.Reports.TryGetValue(reportId, out var variables) ? variables.ToList() : null;
        }
    }

    #endregion

    #region 链接（S2F35）

    /// <summary>
    /// S2F35 事件挂报告 → LRACK。报告表为空 = 摘掉这个事件挂的全部报告。
    /// 事件要存在、报告要都定义过；已经挂了报告的事件不能再挂（先摘再挂）。
    /// </summary>
    public byte LinkReports(IReadOnlyList<(uint EventId, IReadOnlyList<uint> Reports)> links, Func<uint, bool> eventExists)
    {
        lock (_gate)
        {
            var seen = new HashSet<uint>();
            foreach (var (eventId, reports) in links)
            {
                if (!eventExists(eventId))
                {
                    return EventNotExist;
                }

                if (reports.Count == 0)
                {
                    continue;
                }

                bool linked = _config.Links.TryGetValue(eventId, out var existing) && existing.Count > 0;
                if (linked || !seen.Add(eventId))
                {
                    return LinkAlreadyDefined;
                }

                if (reports.Any(report => !_config.Reports.ContainsKey(report)))
                {
                    return ReportNotExist;
                }
            }

            foreach (var (eventId, reports) in links)
            {
                if (reports.Count == 0)
                {
                    _config.Links.Remove(eventId);
                }
                else
                {
                    _config.Links[eventId] = reports.ToList();
                }
            }

            return Accepted;
        }
    }

    /// <summary>一个事件挂的报告（报告号 + 变量），按挂的先后；没挂返回空表。</summary>
    public IReadOnlyList<(uint ReportId, IReadOnlyList<uint> Variables)> ReportsOf(uint eventId)
    {
        lock (_gate)
        {
            if (!_config.Links.TryGetValue(eventId, out var reports))
            {
                return [];
            }

            return reports
                .Where(report => _config.Reports.ContainsKey(report))
                .Select(report => (report, (IReadOnlyList<uint>)_config.Reports[report].ToList()))
                .ToList();
        }
    }

    #endregion

    #region 开关（S2F37 / S5F3）

    /// <summary>
    /// S2F37 开关事件 → ERACK。事件表为空 = 全部事件。有一个事件号不存在就整条不生效。
    /// </summary>
    public byte EnableEvents(bool enable, IReadOnlyList<uint> eventIds, IReadOnlyCollection<uint> allEvents)
    {
        lock (_gate)
        {
            var targets = eventIds.Count == 0 ? allEvents.ToList() : eventIds.ToList();
            if (eventIds.Count > 0 && targets.Any(id => !allEvents.Contains(id)))
            {
                return Denied;
            }

            Toggle(_config.DisabledEvents, targets, enable);
            return Accepted;
        }
    }

    public bool IsEventEnabled(uint eventId)
    {
        lock (_gate)
        {
            return !_config.DisabledEvents.Contains(eventId);
        }
    }

    /// <summary>
    /// S5F3 开关报警 → ACKC5。alarmId 为 null = 全部报警；报警号不存在回 1。
    /// </summary>
    public byte EnableAlarms(bool enable, uint? alarmId, IReadOnlyCollection<uint> allAlarms)
    {
        lock (_gate)
        {
            if (alarmId is not null && !allAlarms.Contains(alarmId.Value))
            {
                return Denied;
            }

            List<uint> targets = alarmId is null ? allAlarms.ToList() : new List<uint> { alarmId.Value };
            Toggle(_config.DisabledAlarms, targets, enable);
            return Accepted;
        }
    }

    public bool IsAlarmEnabled(uint alarmId)
    {
        lock (_gate)
        {
            return !_config.DisabledAlarms.Contains(alarmId);
        }
    }

    private static void Toggle(List<uint> disabled, IEnumerable<uint> targets, bool enable)
    {
        foreach (uint id in targets)
        {
            if (enable)
            {
                disabled.RemoveAll(item => item == id);
            }
            else if (!disabled.Contains(id))
            {
                disabled.Add(id);
            }
        }
    }

    #endregion

    #region 缓存范围（S2F43）

    /// <summary>
    /// S2F43 设缓存范围（已经由 E30 查过合法）：表为空 = 什么都不缓存；某个 Stream 的 Function 表为空 = 这个 Stream 都缓存。
    /// </summary>
    public void SetSpoolStreams(IReadOnlyList<(byte Stream, IReadOnlyList<byte> Functions)> streams)
    {
        lock (_gate)
        {
            _config.SpoolStreams.Clear();
            foreach (var (stream, functions) in streams)
            {
                _config.SpoolStreams[stream] = functions.Distinct().ToList();
            }
        }
    }

    /// <summary>这条报文断了通讯要不要缓存。</summary>
    public bool IsSpoolable(byte stream, byte function)
    {
        lock (_gate)
        {
            return _config.SpoolStreams.TryGetValue(stream, out var functions)
                && (functions.Count == 0 || functions.Contains(function));
        }
    }

    /// <summary>Host 设过要缓存的报文没有（一条都没有就不用开缓存）。</summary>
    public bool HasSpoolStreams
    {
        get
        {
            lock (_gate)
            {
                return _config.SpoolStreams.Count > 0;
            }
        }
    }

    #endregion
}
