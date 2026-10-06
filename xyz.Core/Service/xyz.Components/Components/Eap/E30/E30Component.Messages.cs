using System.Runtime.InteropServices;
using xyz.Common.Log;
using xyz.Components.Collectors;
using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Secs;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// E30 的数据报文：SV（S1F3 / S1F11）、DV 和事件名单（S1F21 / S1F23）、EC（S2F13 / S2F15 / S2F29）、时间（S2F17 / S2F31）、
/// 报告定义（S2F33 / S2F35 / S2F37 / S2F39）、远程命令（S2F41）、缓存（S2F43 / S6F23）、报警（S5F3 / S5F5 / S5F7）、
/// 按需要报告（S6F15 / S6F17 / S6F19 / S6F21）。都在链路的派发线程上跑。
/// </summary>
public partial class E30Component
{
    /// <summary>EAC：收下。</summary>
    private const byte ConstantAccepted = 0;

    /// <summary>EAC 1：有 ECID 不存在。</summary>
    private const byte ConstantNotExist = 1;

    /// <summary>EAC 2：忙（存不进去）。</summary>
    private const byte ConstantBusy = 2;

    /// <summary>EAC 3：有值超出范围（格式不对也算）。</summary>
    private const byte ConstantOutOfRange = 3;

    /// <summary>TIACK：0 收下、1 没改成。</summary>
    private const byte TimeAccepted = 0;

    private const byte TimeNotDone = 1;

    /// <summary>DRACK / LRACK 2：格式不对。</summary>
    private const byte InvalidFormat = 2;

    /// <summary>GRANT：可以发（HSMS 没有分块的限制，多大都收）。</summary>
    private const byte Granted = 0;

    /// <summary>HCACK 1：没有这个命令。</summary>
    private const byte CommandNotExist = 1;

    /// <summary>RSPACK：0 收下、1 不收。</summary>
    private const byte SpoolResetAccepted = 0;

    private const byte SpoolResetRejected = 1;

    /// <summary>STRACK：1 这个 Stream 不许缓存（S1）、2 不认识的 Stream、3 不认识的 Function、4 给的是 secondary。</summary>
    private const byte SpoolNotAllowed = 1;

    private const byte SpoolUnknownStream = 2;

    private const byte SpoolUnknownFunction = 3;

    private const byte SpoolSecondary = 4;

    /// <summary>RSDC：0 发缓存、1 清缓存。</summary>
    private const byte TransmitSpool = 0;

    private const byte PurgeSpool = 1;

    /// <summary>RSDA：0 收下、1 忙（正在发）、2 没有缓存。</summary>
    private const byte SpoolRequestAccepted = 0;

    private const byte SpoolBusy = 1;

    private const byte NoSpoolData = 2;

    /// <summary>ALED 最高位：开报警。</summary>
    private const byte AlarmEnableBit = 0x80;

    /// <summary>ACKC5：0 收下、1 出错（报警号不存在）。</summary>
    private const byte AlarmAccepted = 0;

    /// <summary>
    /// 设备会主动发、断了通讯能缓存的报文：S5F1 报警、S6F11 事件报告。S2F43 只认这几条。
    /// </summary>
    private static readonly IReadOnlyDictionary<byte, byte[]> SpoolablePrimaries = new Dictionary<byte, byte[]>
    {
        [5] = [1],
        [6] = [11],
    };

    private void RegisterHandlers(HsmsComponent link)
    {
        link.Handle(1, 1, AreYouThere);
        link.Handle(1, 3, StatusRequest);
        link.Handle(1, 11, StatusNamelist);
        link.Handle(1, 13, EstablishCommunications);
        link.Handle(1, 15, HostRequestOffline);
        link.Handle(1, 17, HostRequestOnline);
        link.Handle(1, 21, DataNamelist);
        link.Handle(1, 23, EventNamelist);
        link.Handle(2, 13, ConstantRequest);
        link.Handle(2, 15, ConstantSend);
        link.Handle(2, 17, TimeRequest);
        link.Handle(2, 29, ConstantNamelist);
        link.Handle(2, 31, TimeSet);
        link.Handle(2, 33, DefineReports);
        link.Handle(2, 35, LinkReports);
        link.Handle(2, 37, EnableEvents);
        link.Handle(2, 39, Inquire);
        link.Handle(2, 41, HostCommand);
        link.Handle(2, 43, ResetSpooling);
        link.Handle(5, 3, EnableAlarms);
        link.Handle(5, 5, ListAlarms);
        link.Handle(5, 7, ListEnabledAlarms);
        link.Handle(6, 15, EventReportRequest);
        link.Handle(6, 17, AnnotatedEventReportRequest);
        link.Handle(6, 19, IndividualReportRequest);
        link.Handle(6, 21, AnnotatedIndividualReportRequest);
        link.Handle(6, 23, SpooledDataRequest);
    }

    #region S1：SV、名单

    /// <summary>
    /// S1F3 状态查询 → S1F4：按请求里的 SVID 顺序给值（空表 = 全部在用、标了上传的）；
    /// 查不到、没标上传的号回空 ASCII 占位（跟请求对齐）并记警告。
    /// </summary>
    private SecsReply StatusRequest(HsmsMessage message)
    {
        var collectors = GemCollectors.Current;
        if (collectors is null)
        {
            return SecsReply.Of(SecsItem.L());
        }

        var ids = RequestedIds(message, "SVID");
        if (ids.Count == 0)
        {
            ids = collectors.Sv.Collect().Where(sv => sv.Visible).Select(sv => (uint)sv.Svid).ToList();
        }

        var values = new List<SecsItem>();
        foreach (uint svid in ids)
        {
            if (svid <= int.MaxValue && collectors.Sv.TryRead((int)svid, out var row, out object? raw) && row is not null && row.Visible)
            {
                values.Add(GemValue.From(raw, row.Format));
            }
            else
            {
                LogHelper.Warn(Name, $"S1F3 问了不认识的 SVID {svid}，回空占位");
                values.Add(SecsItem.A(string.Empty));
            }
        }

        return SecsReply.Of(SecsItem.L(values));
    }

    /// <summary>S1F11 SV 名单 → S1F12 L[n]{L[3]{SVID, SVNAME, UNITS}}；空表 = 全部。不认识的号名字、单位给空。</summary>
    private SecsReply StatusNamelist(HsmsMessage message)
    {
        var collectors = GemCollectors.Current;
        var all = collectors?.Sv.Collect().Where(sv => sv.Visible).ToList() ?? [];
        var ids = RequestedIds(message, "SVID");
        if (ids.Count == 0)
        {
            ids = all.Select(sv => (uint)sv.Svid).ToList();
        }

        return SecsReply.Of(SecsItem.L(ids.Select(svid =>
        {
            var sv = all.FirstOrDefault(item => item.Svid == svid);
            return SecsItem.L(SecsItem.U4(svid), SecsItem.A(GemValue.Ascii(sv?.Name ?? string.Empty)),
                SecsItem.A(GemValue.Ascii(sv?.Unit ?? string.Empty)));
        })));
    }

    /// <summary>S1F21 DV 名单 → S1F22 L[n]{L[3]{VID, DVVALNAME, UNITS}}；空表 = 全部。</summary>
    private SecsReply DataNamelist(HsmsMessage message)
    {
        var all = GemCollectors.Current?.Dv.Collect() ?? [];
        var ids = RequestedIds(message, "VID");
        if (ids.Count == 0)
        {
            ids = all.Select(dv => (uint)dv.Dvid).ToList();
        }

        return SecsReply.Of(SecsItem.L(ids.Select(dvid =>
        {
            var dv = all.FirstOrDefault(item => item.Dvid == dvid);
            return SecsItem.L(SecsItem.U4(dvid), SecsItem.A(GemValue.Ascii(dv?.Name ?? string.Empty)),
                SecsItem.A(GemValue.Ascii(dv?.Unit ?? string.Empty)));
        })));
    }

    /// <summary>S1F23 事件名单 → S1F24 L[n]{L[3]{CEID, CENAME, L[a]{VID}}}（VID 是事件带的 DV）；空表 = 全部。</summary>
    private SecsReply EventNamelist(HsmsMessage message)
    {
        var all = GemCollectors.Current?.Event.Collect() ?? [];
        var ids = RequestedIds(message, "CEID");
        if (ids.Count == 0)
        {
            ids = all.Select(item => (uint)item.Ceid).ToList();
        }

        return SecsReply.Of(SecsItem.L(ids.Select(ceid =>
        {
            var item = all.FirstOrDefault(row => row.Ceid == ceid);
            return SecsItem.L(SecsItem.U4(ceid), SecsItem.A(GemValue.Ascii(item?.Name ?? string.Empty)),
                SecsItem.L((item?.Dvids ?? []).Select(dvid => SecsItem.U4((uint)dvid))));
        })));
    }

    /// <summary>请求里的编号表：没带体或空表返回空（= 全部）。</summary>
    private static List<uint> RequestedIds(HsmsMessage message, string what)
    {
        var body = message.Body;
        if (body is null || body.Count == 0)
        {
            return [];
        }

        return SecsRead.List(body, what).Select(item => SecsRead.Id(item, what)).ToList();
    }

    #endregion

    #region S2：EC、时间

    /// <summary>S2F13 EC 查询 → S2F14 L[n]{ECV}；空表 = 全部在用、标了上传的。不认识的号回空 ASCII。</summary>
    private SecsReply ConstantRequest(HsmsMessage message)
    {
        var collectors = GemCollectors.Current;
        if (collectors is null)
        {
            return SecsReply.Of(SecsItem.L());
        }

        var ids = RequestedIds(message, "ECID");
        if (ids.Count == 0)
        {
            ids = collectors.Ec.Collect().Where(ec => ec.Visible).Select(ec => (uint)ec.Ecid).ToList();
        }

        return SecsReply.Of(SecsItem.L(ids.Select(ecid =>
        {
            var ec = ecid <= int.MaxValue ? collectors.Ec.ByEcid((int)ecid) : null;
            return ec is not null && ec.Visible ? GemValue.FromText(ec.Value, ec.Format) : SecsItem.A(string.Empty);
        })));
    }

    /// <summary>
    /// S2F15 Host 改 EC → S2F16 EAC：要么全改、要么都不改——先全查（号在不在、格式和范围对不对），都对了再一项项改（写回 ec.xml）。
    /// Host 改的不报"操作员改了设备常量"。
    /// </summary>
    private SecsReply ConstantSend(HsmsMessage message)
    {
        var items = SecsRead.List(SecsRead.Body(message), "S2F15");
        var collectors = GemCollectors.Current;
        var store = EcComponent.Current;
        if (collectors is null || store is null)
        {
            return SecsReply.Of(SecsItem.B(ConstantNotExist));
        }

        var changes = new List<(string Path, string Name, string Value)>();
        foreach (var item in items)
        {
            var pair = SecsRead.List(item, "L{ECID, ECV}", 2);
            uint ecid = SecsRead.Id(pair[0], "ECID");
            if (ecid > int.MaxValue || !collectors.Ec.TryLocate((int)ecid, out string path, out string name))
            {
                return SecsReply.Of(SecsItem.B(ConstantNotExist));
            }

            var row = collectors.Ec.ByEcid((int)ecid);
            string? text = row is null ? null : GemValue.ToEcText(pair[1], row.Format);
            if (row is null || !row.Visible)
            {
                return SecsReply.Of(SecsItem.B(ConstantNotExist));
            }

            if (text is null)
            {
                return SecsReply.Of(SecsItem.B(ConstantOutOfRange));
            }

            var check = store.Check(path, name, text);
            if (check == EcSetResult.NotFound)
            {
                return SecsReply.Of(SecsItem.B(ConstantNotExist));
            }

            if (check != EcSetResult.Ok)
            {
                return SecsReply.Of(SecsItem.B(ConstantOutOfRange));
            }

            changes.Add((path, name, text));
        }

        byte eac = ConstantAccepted;
        _applyingHostConstants = true;
        try
        {
            foreach (var (path, name, value) in changes)
            {
                if (store.TrySet(path, name, value, out _) == EcSetResult.SaveFailed)
                {
                    eac = ConstantBusy;
                }
            }
        }
        finally
        {
            _applyingHostConstants = false;
        }

        LogHelper.Info(Name, $"Host 改了 {changes.Count} 项 EC" + (eac == ConstantAccepted ? string.Empty : "（有的没存进去）"));
        return SecsReply.Of(SecsItem.B(eac));
    }

    /// <summary>
    /// S2F29 EC 名单 → S2F30 L[n]{L[6]{ECID, ECNAME, ECMIN, ECMAX, ECDEF, UNITS}}；空表 = 全部。上下限、默认值按 EC 的格式给。
    /// </summary>
    private SecsReply ConstantNamelist(HsmsMessage message)
    {
        var all = GemCollectors.Current?.Ec.Collect().Where(ec => ec.Visible).ToList() ?? [];
        var ids = RequestedIds(message, "ECID");
        if (ids.Count == 0)
        {
            ids = all.Select(ec => (uint)ec.Ecid).ToList();
        }

        return SecsReply.Of(SecsItem.L(ids.Select(ecid =>
        {
            var ec = all.FirstOrDefault(item => item.Ecid == ecid);
            if (ec is null)
            {
                return SecsItem.L(SecsItem.U4(ecid), SecsItem.A(string.Empty), SecsItem.A(string.Empty), SecsItem.A(string.Empty),
                    SecsItem.A(string.Empty), SecsItem.A(string.Empty));
            }

            return SecsItem.L(SecsItem.U4(ecid), SecsItem.A(GemValue.Ascii(ec.Name)), Limit(ec.Min, ec.Format), Limit(ec.Max, ec.Format),
                Limit(ec.Default, ec.Format), SecsItem.A(GemValue.Ascii(ec.Unit)));
        })));

        static SecsItem Limit(string text, string format)
        {
            return string.IsNullOrEmpty(text) ? SecsItem.A(string.Empty) : GemValue.FromText(text, format);
        }
    }

    /// <summary>S2F17 时间查询 → S2F18 TIME（格式按 EC TimeFormat）。</summary>
    private SecsReply TimeRequest(HsmsMessage message)
    {
        return SecsReply.Of(SecsItem.A(GemValue.Time(DateTime.Now, TimeFormat)));
    }

    /// <summary>
    /// S2F31 对时 → S2F32 TIACK：时间格式不对回 1；SC ApplyHostTime 为 False 时只答收下、不改本机时钟（现场的规矩），
    /// 为 True 时改本机时钟，改不了（没权限）回 1。
    /// </summary>
    private SecsReply TimeSet(HsmsMessage message)
    {
        string text = SecsRead.Text(SecsRead.Body(message), "TIME");
        if (!GemValue.TryParseTime(text, out var time))
        {
            LogHelper.Warn(Name, $"Host 对时的时间格式不对：{text}");
            return SecsReply.Of(SecsItem.B(TimeNotDone));
        }

        if (!ApplyHostTime)
        {
            LogHelper.Info(Name, $"Host 对时 {text}：按设定只答收下，不改本机时钟");
            return SecsReply.Of(SecsItem.B(TimeAccepted));
        }

        if (!SetLocalClock(time))
        {
            LogHelper.Warn(Name, $"Host 对时 {text}：改本机时钟没成（宿主可能没有改时间的权限）");
            return SecsReply.Of(SecsItem.B(TimeNotDone));
        }

        LogHelper.Info(Name, $"Host 对时：本机时钟改成 {time:yyyy-MM-dd HH:mm:ss.ff}");
        return SecsReply.Of(SecsItem.B(TimeAccepted));
    }

    /// <summary>改本机时钟（Windows SetLocalTime）；别的系统或没权限返回 false。</summary>
    private static bool SetLocalClock(DateTime time)
    {
        try
        {
            var system = new SystemTime
            {
                Year = (ushort)time.Year,
                Month = (ushort)time.Month,
                DayOfWeek = (ushort)time.DayOfWeek,
                Day = (ushort)time.Day,
                Hour = (ushort)time.Hour,
                Minute = (ushort)time.Minute,
                Second = (ushort)time.Second,
                Milliseconds = (ushort)time.Millisecond,
            };
            return SetLocalTime(ref system);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLocalTime(ref SystemTime time);

    #endregion

    #region S2：报告定义、远程命令、缓存范围

    /// <summary>
    /// S2F33 定义报告 → S2F34 DRACK：L[2]{DATAID, L[a]{L[2]{RPTID, L[b]{VID}}}}。结构不对回 2，规则见 GemBook。改了就存盘。
    /// </summary>
    private SecsReply DefineReports(HsmsMessage message)
    {
        List<(uint ReportId, IReadOnlyList<uint> Variables)> reports;
        try
        {
            var body = SecsRead.List(SecsRead.Body(message), "S2F33", 2);
            reports = SecsRead.List(body[1], "报告表").Select(item =>
            {
                var report = SecsRead.List(item, "L{RPTID, L{VID}}", 2);
                return (SecsRead.Id(report[0], "RPTID"),
                    (IReadOnlyList<uint>)SecsRead.List(report[1], "VID 表").Select(vid => SecsRead.Id(vid, "VID")).ToList());
            }).ToList();
        }
        catch (SecsException exception)
        {
            LogHelper.Warn(Name, $"S2F33 格式不对：{exception.Message}");
            return SecsReply.Of(SecsItem.B(InvalidFormat));
        }

        byte drack = _book.DefineReports(reports, VariableExists);
        if (drack == GemBook.Accepted)
        {
            SaveConfig();
        }

        return SecsReply.Of(SecsItem.B(drack));
    }

    /// <summary>
    /// S2F35 事件挂报告 → S2F36 LRACK：L[2]{DATAID, L[a]{L[2]{CEID, L[b]{RPTID}}}}。结构不对回 2。
    /// </summary>
    private SecsReply LinkReports(HsmsMessage message)
    {
        List<(uint EventId, IReadOnlyList<uint> Reports)> links;
        try
        {
            var body = SecsRead.List(SecsRead.Body(message), "S2F35", 2);
            links = SecsRead.List(body[1], "链接表").Select(item =>
            {
                var link = SecsRead.List(item, "L{CEID, L{RPTID}}", 2);
                return (SecsRead.Id(link[0], "CEID"),
                    (IReadOnlyList<uint>)SecsRead.List(link[1], "RPTID 表").Select(report => SecsRead.Id(report, "RPTID")).ToList());
            }).ToList();
        }
        catch (SecsException exception)
        {
            LogHelper.Warn(Name, $"S2F35 格式不对：{exception.Message}");
            return SecsReply.Of(SecsItem.B(InvalidFormat));
        }

        var events = AllEventIds().ToHashSet();
        byte lrack = _book.LinkReports(links, events.Contains);
        if (lrack == GemBook.Accepted)
        {
            SaveConfig();
        }

        return SecsReply.Of(SecsItem.B(lrack));
    }

    /// <summary>S2F37 开关事件 → S2F38 ERACK：L[2]{CEED, L[n]{CEID}}，事件表空 = 全部。</summary>
    private SecsReply EnableEvents(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S2F37", 2);
        bool enable = SecsRead.Flag(body[0], "CEED");
        var ids = SecsRead.List(body[1], "CEID 表").Select(item => SecsRead.Id(item, "CEID")).ToList();
        byte erack = _book.EnableEvents(enable, ids, AllEventIds().ToHashSet());
        if (erack == GemBook.Accepted)
        {
            SaveConfig();
        }

        return SecsReply.Of(SecsItem.B(erack));
    }

    /// <summary>S2F39 多块询问 → S2F40 GRANT=0：HSMS 一帧多大都能收，直接让发。</summary>
    private SecsReply Inquire(HsmsMessage message)
    {
        return SecsReply.Of(SecsItem.B(Granted));
    }

    /// <summary>
    /// S2F41 远程命令 → S2F42 L[2]{HCACK, L[0]}：本机没有定义远程命令（Job、载具的控制走 E94 / E40 / E87 的报文），一律回 HCACK=1（没有这个命令）。
    /// </summary>
    private SecsReply HostCommand(HsmsMessage message)
    {
        var body = message.Body;
        var rcmd = body is not null && body.Format == SecsFormat.List && body.Count > 0 ? body.Items[0] : null;
        string name = rcmd is null ? "（没带 RCMD）" : rcmd.Format is SecsFormat.Ascii or SecsFormat.Jis8 ? rcmd.GetString() : rcmd.ToString();
        LogHelper.Warn(Name, $"Host 的远程命令本机没有：{name}");
        return SecsReply.Of(SecsItem.L(SecsItem.B(CommandNotExist), SecsItem.L()));
    }

    /// <summary>
    /// S2F43 设缓存范围 → S2F44 L[2]{RSPACK, L[m]{L[3]{STRID, STRACK, L[n]{FCNID}}}}：L[m]{L[2]{STRID, L[n]{FCNID}}}，
    /// 表空 = 什么都不缓存，Function 表空 = 这个 Stream 能缓存的都缓存。只认设备会主动发的 S5F1、S6F11；
    /// 有一项不对整条不收，回的表里列出不对的 Stream 和它的原因。
    /// </summary>
    private SecsReply ResetSpooling(HsmsMessage message)
    {
        var request = new List<(byte Stream, IReadOnlyList<byte> Functions)>();
        var errors = new List<SecsItem>();
        foreach (var item in SecsRead.List(SecsRead.Body(message), "S2F43"))
        {
            var entry = SecsRead.List(item, "L{STRID, L{FCNID}}", 2);
            byte stream = SecsRead.Code(entry[0], "STRID");
            var functions = SecsRead.List(entry[1], "FCNID 表").Select(function => SecsRead.Code(function, "FCNID")).ToList();
            byte? error = null;
            var bad = new List<byte>();
            if (stream == 1)
            {
                error = SpoolNotAllowed;
            }
            else if (!SpoolablePrimaries.TryGetValue(stream, out var known))
            {
                error = SpoolUnknownStream;
            }
            else
            {
                foreach (byte function in functions)
                {
                    if (function % 2 == 0)
                    {
                        error ??= SpoolSecondary;
                        bad.Add(function);
                    }
                    else if (!known.Contains(function))
                    {
                        error ??= SpoolUnknownFunction;
                        bad.Add(function);
                    }
                }
            }

            if (error is not null)
            {
                errors.Add(SecsItem.L(SecsItem.U1(stream), SecsItem.B(error.Value), SecsItem.L(bad.Select(function => SecsItem.U1(function)))));
                continue;
            }

            request.Add((stream, functions));
        }

        if (errors.Count > 0)
        {
            return SecsReply.Of(SecsItem.L(SecsItem.B(SpoolResetRejected), SecsItem.L(errors)));
        }

        _book.SetSpoolStreams(request);
        SaveConfig();
        LogHelper.Info(Name, request.Count == 0
            ? "Host 设缓存范围：什么都不缓存"
            : $"Host 设缓存范围：{string.Join("、", request.Select(entry => $"S{entry.Stream}" + (entry.Functions.Count == 0 ? string.Empty : $"F{string.Join("/F", entry.Functions)}")))}");
        return SecsReply.Of(SecsItem.L(SecsItem.B(SpoolResetAccepted), SecsItem.L()));
    }

    #endregion

    #region S5：报警

    /// <summary>S5F3 开关报警 → S5F4 ACKC5：L[2]{ALED, ALID}，ALID 零长度 = 全部报警。</summary>
    private SecsReply EnableAlarms(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S5F3", 2);
        bool enable = (SecsRead.Code(body[0], "ALED") & AlarmEnableBit) != 0;
        uint? alid = body[1].Count == 0 ? null : SecsRead.Id(body[1], "ALID");
        byte ack = _book.EnableAlarms(enable, alid, AllAlarmIds().ToHashSet());
        if (ack == AlarmAccepted)
        {
            SaveConfig();
        }

        return SecsReply.Of(SecsItem.B(ack));
    }

    /// <summary>S5F5 列报警 → S5F6 L[n]{L[3]{ALCD, ALID, ALTX}}：ALID 向量，空 = 全部；ALCD 最高位表示现在在报着。</summary>
    private SecsReply ListAlarms(HsmsMessage message)
    {
        var body = message.Body;
        var ids = body is null || body.Count == 0 ? AllAlarmIds() : SecsRead.Ids(body, "ALID");
        return SecsReply.Of(AlarmList(ids));
    }

    /// <summary>S5F7 列开着的报警 → S5F8，格式同 S5F6。</summary>
    private SecsReply ListEnabledAlarms(HsmsMessage message)
    {
        return SecsReply.Of(AlarmList(AllAlarmIds().Where(_book.IsAlarmEnabled).ToList()));
    }

    private static SecsItem AlarmList(IReadOnlyList<uint> ids)
    {
        var definitions = GemCollectors.Current?.Alarm.Definitions ?? [];
        var active = ActiveAlarmIds().ToHashSet();
        return SecsItem.L(ids.Select(alid =>
        {
            var definition = definitions.FirstOrDefault(row => row.Enabled && row.Id == alid);
            byte alcd = active.Contains(alid) ? AlarmSetBit : (byte)0;
            return SecsItem.L(SecsItem.B(alcd), SecsItem.U4(alid), SecsItem.A(definition is null ? string.Empty : AlarmText(definition.Name)));
        }));
    }

    #endregion

    #region S6：按需要报告、缓存

    /// <summary>S6F15 要某个事件的报告 → S6F16 L[3]{DATAID, CEID, L[a]{L[2]{RPTID, L[b]{V}}}}（现值，DV 报空）。</summary>
    private SecsReply EventReportRequest(HsmsMessage message)
    {
        uint ceid = SecsRead.Id(SecsRead.Body(message), "CEID");
        return SecsReply.Of(SecsItem.L(SecsItem.U4(NextDataId()), SecsItem.U4(ceid), BuildReports(ceid, null, annotated: false)));
    }

    /// <summary>S6F17 要某个事件的带注释报告 → S6F18：同 S6F16，每个值是 L[2]{VID, V}。</summary>
    private SecsReply AnnotatedEventReportRequest(HsmsMessage message)
    {
        uint ceid = SecsRead.Id(SecsRead.Body(message), "CEID");
        return SecsReply.Of(SecsItem.L(SecsItem.U4(NextDataId()), SecsItem.U4(ceid), BuildReports(ceid, null, annotated: true)));
    }

    /// <summary>S6F19 要单个报告 → S6F20 L[b]{V}；报告没定义回空表。</summary>
    private SecsReply IndividualReportRequest(HsmsMessage message)
    {
        uint reportId = SecsRead.Id(SecsRead.Body(message), "RPTID");
        return SecsReply.Of(ReportValues(_book.Report(reportId) ?? [], null, annotated: false));
    }

    /// <summary>S6F21 要单个带注释报告 → S6F22 L[b]{L[2]{VID, V}}。</summary>
    private SecsReply AnnotatedIndividualReportRequest(HsmsMessage message)
    {
        uint reportId = SecsRead.Id(SecsRead.Body(message), "RPTID");
        return SecsReply.Of(ReportValues(_book.Report(reportId) ?? [], null, annotated: true));
    }

    /// <summary>
    /// S6F23 要缓存 / 清缓存 → S6F24 RSDA：正在发回 1；没有缓存回 2；RSDC=0 按先后发（一次最多 EC MaxSpoolTransmit 条），RSDC=1 清掉。
    /// 发和清都排在发送线程上做，这里当场回收下。
    /// </summary>
    private SecsReply SpooledDataRequest(HsmsMessage message)
    {
        byte rsdc = SecsRead.Code(SecsRead.Body(message), "RSDC");
        if (rsdc is not (TransmitSpool or PurgeSpool))
        {
            throw new SecsException($"RSDC 只能是 0 或 1，收到 {rsdc}");
        }

        var sender = _sender;
        var spool = _spool;
        if (sender is null || spool is null)
        {
            return SecsReply.Of(SecsItem.B(NoSpoolData));
        }

        if (sender.IsTransmitting)
        {
            return SecsReply.Of(SecsItem.B(SpoolBusy));
        }

        if (!spool.IsActive && spool.CountActual == 0)
        {
            return SecsReply.Of(SecsItem.B(NoSpoolData));
        }

        if (rsdc == TransmitSpool)
        {
            LogHelper.Info(Name, $"Host 要缓存：{spool.CountActual} 条");
            sender.Transmit(MaxSpoolTransmit);
        }
        else
        {
            sender.Purge();
        }

        return SecsReply.Of(SecsItem.B(SpoolRequestAccepted));
    }

    #endregion
}
