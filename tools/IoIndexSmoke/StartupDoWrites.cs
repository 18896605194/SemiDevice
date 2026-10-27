using System.Diagnostics;
using xyz.Elements;
using xyz.Elements.Elements;
using xyz.Elements.Interfaces;
using xyz.Configs.Models;
using xyz.Tools;

/// <summary>
/// 开机下发 DO：配置解析（DO 不分大小写、分号隔开、写错开机就报）、连上 PLC 满延时才下发、等的时候掉线重新计时、
/// 点表里没有的点跳过、一次开机只下发一次。延时用 EC 默认值 1000 ms（冒烟里没装 EC 元件）。
/// </summary>
static class StartupDoWrites
{
    public static void Run(string directory)
    {
        foreach (string bad in new[] { "DO12", "DO12=flase", "DI12=true", "DO=true", "DOx=true", "DO-1=true", "DO12=true=false" })
        {
            bool thrown = false;
            try
            {
                new ConfiguredIo { StartupDo = bad }.Load();
            }
            catch (InvalidOperationException)
            {
                thrown = true;
            }

            Check(thrown, $"写错的开机 DO（{bad}）装配时要报出来");
        }

        var empty = new ConfiguredIo();
        SmokeIoc.Use<IIo>(empty);
        empty.Load();

        // 点表放进 IO 元件的点表目录：它开机准备时读点表、取 PLC
        string tables = Path.Combine(directory, "startup-io");
        Directory.CreateDirectory(tables);
        File.WriteAllText(Path.Combine(tables, "DO.csv"), "Index,Name\n12,Valve\n13,Lamp\n");

        var plc = new RecordingPlc();
        var previousPlc = IocHelper.GetService<IPlcIo>();
        SmokeIoc.Use<IPlcIo>(plc);
        var io = new ConfiguredIo { StartupDo = " do12 = FALSE ; Do13=true;;DO99=true; ", PointTableDirectory = tables };
        io.Load();
        io.InitElement();
        io.Start();

        // IO 元件开机准备时就取好了 PLC，之后再还原不影响它。
        Thread.Sleep(1500);
        SmokeIoc.Use<IPlcIo>(previousPlc);
        Check(plc.Writes.Count == 0, "PLC 没连上不下发");

        plc.IsConnected = true;
        Thread.Sleep(600);
        plc.IsConnected = false;
        Thread.Sleep(200);
        plc.IsConnected = true;
        var sinceReconnect = Stopwatch.StartNew();
        Thread.Sleep(600);
        Check(plc.Writes.Count == 0, "等延时的时候掉线，重连后要重新计时");

        while (plc.Writes.Count < 2 && sinceReconnect.ElapsedMilliseconds < 5000)
        {
            Thread.Sleep(20);
        }

        Check(sinceReconnect.ElapsedMilliseconds >= 950, "连上满延时才下发");
        var writes = plc.Writes;
        Check(writes.Count == 2 && writes[0] == (12, false) && writes[1] == (13, true),
            "按配置顺序下发，DO 和 true/false 不分大小写，点表里没有的 DO99 跳过");

        plc.IsConnected = false;
        Thread.Sleep(200);
        plc.IsConnected = true;
        Thread.Sleep(1500);
        Check(plc.Writes.Count == 2, "一次开机只下发一次，断线重连不再下发");

        Console.WriteLine("PASS: 开机下发 DO（解析、延时、掉线重新计时、点表外跳过、只下发一次）");
    }

    static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>把装配钩子露出来：冒烟里没有 machine.xml，直接调。</summary>
    sealed class ConfiguredIo : IoElement
    {
        public void Load()
        {
            OnSettingLoaded(new ModuleConfig());
        }
    }

    /// <summary>记下每次 DO 写入；IO 元件的扫描线程写、冒烟主线程读，所以加锁给副本。</summary>
    sealed class RecordingPlc : IPlcIo
    {
        private readonly object _gate = new();
        private readonly List<(int, bool)> _writes = new();
        private volatile bool _connected;

        public bool IsConnected
        {
            get { return _connected; }
            set { _connected = value; }
        }

        public List<(int, bool)> Writes
        {
            get
            {
                lock (_gate)
                {
                    return [.. _writes];
                }
            }
        }

        public bool WriteDo(int index, bool on)
        {
            lock (_gate)
            {
                _writes.Add((index, on));
            }

            return true;
        }

        public bool TryReadDi(int index, out bool on) { on = false; return false; }
        public bool TryReadDo(int index, out bool on) { on = false; return false; }
        public bool TryReadAi(int index, out double value) { value = 0; return false; }
        public bool TryReadAo(int index, out double value) { value = 0; return false; }
        public bool WriteAo(int index, double value) => false;
    }
}
