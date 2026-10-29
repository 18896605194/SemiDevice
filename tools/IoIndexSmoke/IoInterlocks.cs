using xyz.Elements;
using xyz.Elements.Elements;
using xyz.Elements.Interfaces;
using xyz.Configs.Models;
using xyz.Shared.Errors;
using xyz.Tools;

static class IoInterlocks
{
    public static void Run(string directory)
    {
        var previousPlc = IocHelper.GetService<IPlcIo>();
        var previousIo = IocHelper.GetService<IIo>();
        try
        {
            foreach (string bad in new[] { "DI25=false", "DO25=false:DO12=true", "DI25=flase:DO12=true",
                "DI-1=false:DO12=true", "DI25=false:DI12=true", "DI25=false:", "DI25=false:DO12=true,",
                "DI25=false:DO12=true:DO13=true", "DI25=false:DO12=true,DO12=false",
                "DI25=false:DO12=true;DI25=false:DO12=false" })
            {
                bool rejected = false;
                try
                {
                    new TestIo { SaftyIoInterlock = bad }.Load();
                }
                catch (InvalidOperationException)
                {
                    rejected = true;
                }
                Check(rejected, $"invalid rule must fail during assembly: {bad}");
            }

            // 点表放进 IO 元件的点表目录：它开机准备时读点表、取 PLC
            string tables = Path.Combine(directory, "interlock-io");
            Directory.CreateDirectory(tables);
            File.WriteAllText(Path.Combine(tables, "DI.csv"), "Index,Name\n25,Door\n26,Sensor\n");
            File.WriteAllText(Path.Combine(tables, "DO.csv"), "Index,Name\n12,Valve\n13,Valve2\n14,Valve3\n");

            TestIo Create(RecordingPlc plc, string rules)
            {
                SmokeIoc.Use<IPlcIo>(plc);
                var io = new TestIo { SaftyIoInterlock = rules, PointTableDirectory = tables };
                SmokeIoc.Use<IIo>(io);
                io.Load();
                io.InitElement();
                return io;
            }

            var plc = new RecordingPlc();
            plc.Di[25] = false;
            plc.Di[26] = true;
            var io = Create(plc, " di25 = FALSE ： do12 = TRUE ， Do13 = true ； DI26=TrUe:do14=FaLsE;; ");
            io.Tick();
            Check(plc.Writes.SequenceEqual(new[] { (12, true), (13, true), (14, false) }),
                "initial levels force all rules, including false outputs, with mixed case and Chinese separators");
            io.Tick();
            Check(plc.Writes.Count == 3, "confirmed forced values need no redundant writes");
            var blocked = io.WriteDo(12, false);
            Check(blocked.ErrorMessage == ErrorCodes.IoInterlockBlocked
                && blocked.Args.SequenceEqual(new[] { "12", "false", "DI25", "false", "true", "di25 = FALSE : do12 = TRUE , Do13 = true" })
                && plc.Do[12], "active DI blocks opposite command, with the DI, its level and the rule");
            Check(io.WriteDo(12, true).IsSuccess, "forced value is allowed");
            plc.Do[12] = false;
            Check(!io.WriteDo(12, false).IsSuccess && plc.Do[12], "rejected command also restores a deviated output");
            plc.Do[13] = false;
            io.Tick();
            Check(plc.Do[13], "sustained level corrects output drift");
            plc.Di[25] = true;
            io.Tick();
            Check(plc.Do[12] && plc.Do[13], "release does not restore old output values");
            Check(io.WriteDo(12, false).IsSuccess, "release permits opposite command");
            plc.Di.Remove(25);
            Check(io.WriteDo(12, false).ErrorMessage == ErrorCodes.IoInterlockUnknown, "unreadable DI cannot be treated as released");
            io.Tick();
            plc.Di[25] = false;
            plc.IsConnected = false;
            io.Tick();
            Check(!io.WriteDo(12, false).IsSuccess, "disconnected DI cannot authorize a command");
            plc.IsConnected = true;
            plc.FailWrites = true;
            io.Tick();
            plc.FailWrites = false;
            io.Tick();
            Check(plc.Do[12], "reconnect and failed forced writes retry");

            var overlapPlc = new RecordingPlc();
            overlapPlc.Di[25] = false;
            overlapPlc.Di[26] = true;
            var overlap = Create(overlapPlc, "DI25=false:DO12=true;DI26=true:DO12=true");
            overlap.Tick();
            Check(overlapPlc.Writes.Count == 1, "same-value overlapping rules write once");
            overlapPlc.Di[25] = true;
            Check(!overlap.WriteDo(12, false).IsSuccess, "remaining active rule still blocks");
            overlapPlc.Di[26] = false;
            Check(overlap.WriteDo(12, false).IsSuccess, "all rules released permits write before next scan");

            var conflictPlc = new RecordingPlc();
            conflictPlc.Di[25] = false;
            conflictPlc.Di[26] = true;
            var conflict = Create(conflictPlc, "DI25=false:DO12=true;DI26=true:DO12=false");
            conflict.Tick();
            Check(conflict.WriteDo(12, true).ErrorMessage == ErrorCodes.IoInterlockConflict && conflict.WriteDo(12, false).ErrorMessage == ErrorCodes.IoInterlockConflict
                && conflictPlc.Writes.Count == 0, "conflicting active rules block both values without alternating writes");
            conflictPlc.Di.Remove(26);
            conflict.Tick();
            Check(conflictPlc.Writes.Count == 0, "unknown opposing condition cannot be bypassed by enforcement");
            conflictPlc.Di[26] = false;
            conflict.Tick();
            Check(conflictPlc.Do[12], "resolved conflict resumes forcing");

            var startupPlc = new RecordingPlc();
            startupPlc.Di[25] = false;
            var startup = Create(startupPlc, "DI25=false:DO12=true");
            startup.StartupDo = "DO12=false";
            startup.Load();
            startup.Tick();
            Thread.Sleep(1100);
            startup.Tick();
            Check(startupPlc.Writes.All(write => write == (12, true)), "startup DO cannot override interlock");

            var plainPlc = new RecordingPlc();
            var plain = Create(plainPlc, "");
            Check(plain.WriteDo(12, false).IsSuccess, "empty config preserves original behavior");
            Console.WriteLine("PASS: IO interlocks (parsing, case, multiple rules, enforcement, blocking, release, retries, conflicts, startup)");
        }
        finally
        {
            SmokeIoc.Use<IPlcIo>(previousPlc);
            SmokeIoc.Use<IIo>(previousIo);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class TestIo : IoElement
    {
        public void Load() => OnSettingLoaded(new ModuleConfig());
        public void Tick() => ScanOnce();
    }

    private sealed class RecordingPlc : IPlcIo
    {
        public bool IsConnected { get; set; } = true;
        public bool FailWrites { get; set; }
        public Dictionary<int, bool> Di { get; } = [];
        public Dictionary<int, bool> Do { get; } = [];
        public List<(int, bool)> Writes { get; } = [];
        public bool TryReadDi(int index, out bool on)
        {
            on = false;
            return IsConnected && Di.TryGetValue(index, out on);
        }

        public bool TryReadDo(int index, out bool on)
        {
            on = false;
            return IsConnected && Do.TryGetValue(index, out on);
        }

        public bool WriteDo(int index, bool on)
        {
            if (!IsConnected || FailWrites)
            {
                return false;
            }

            Writes.Add((index, on));
            Do[index] = on;
            return true;
        }

        public bool TryReadAi(int index, out double value)
        {
            value = 0;
            return false;
        }

        public bool TryReadAo(int index, out double value)
        {
            value = 0;
            return false;
        }

        public bool WriteAo(int index, double value) => false;
    }
}
