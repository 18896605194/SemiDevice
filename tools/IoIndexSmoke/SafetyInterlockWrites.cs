using System.Reflection;
using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs.Models;

/// <summary>
/// 安全联锁（SC）冒烟：解析、DI 组件/原始电平取状态、条件成立时强制 DO、下发拦截、读不到 DI 和配置冲突时的安全优先。
/// </summary>
static class SafetyInterlockWrites
{
    public static void Run(string directory)
    {
        var previousPlc = PlcComponent.Current;
        var previousIo = IoComponent.Current;
        try
        {
            ParseErrors();

            var csv = Path.Combine(directory, "safety-interlock.csv");
            File.WriteAllText(csv,
                "Index,Name\n" +
                "2,Guard\n" +
                "3,Di3\n" +
                "4,Di4\n" +
                "5,Di5\n" +
                "12,ValveA\n" +
                "13,ValveB\n");

            GateAndForce(csv);
            RawDiFallback(csv);
            UnknownAndConflict(csv);
            Console.WriteLine("PASS: 安全联锁（解析、DI 组件取状态、强制跟随、下发拦截、未知 DI 与规则冲突）");
        }
        finally
        {
            PlcComponent.Current = previousPlc;
            IoComponent.Current = previousIo;
        }
    }

    private static void ParseErrors()
    {
        foreach (string bad in new[]
                 {
                     "DO12=true",
                     "Di2:DO12=true",
                     "Di=false:DO12=true",
                     "Dix=false:DO12=true",
                     "Di2=false:DO12",
                     "Di2=false:DOx=true",
                     "Di2=false:DO12=flase",
                     "Di2=false:DO12=true,DO12=false",
                     "Di2=false:",
                 })
        {
            bool thrown = false;
            try
            {
                new ConfiguredIo { SafetyInterlock = bad }.Load();
            }
            catch (InvalidOperationException)
            {
                thrown = true;
            }

            Check(thrown, $"写错的 SC（{bad}）装配时要报出来");
        }

        new ConfiguredIo { SafetyInterlock = "Di2=false：DO12=true，DO13=true；" }.Load();
    }

    private static void GateAndForce(string csv)
    {
        var io = new ConfiguredIo { SafetyInterlock = "Di2=false：DO12=true，DO13=true；" };
        io.Load();
        io.Di.Load(csv);
        io.Do.Load(csv);

        var sensor = new ScanningDi { DiIndex = 2 };
        SetFullPath(sensor, "Safety.TestGuard");

        var plc = new StatePlc();
        PlcComponent.Current = plc;

        plc.Di[2] = false;
        sensor.Tick();
        Check(sensor.CurrentLevel == false, "DI 组件要读到原始电平 false");

        io.Tick();
        Check(plc.Do[12] && plc.Do[13], "Di2=false 时 SC 要把 DO12/DO13 强制为 true");

        Check(!io.WriteDo(12, false), "Di2=false 时不允许下发 DO12=false");
        Check(!io.WriteDo(13, false), "Di2=false 时不允许下发 DO13=false");
        Check(io.WriteDo(12, true), "Di2=false 时允许下发 DO12=true");
        Check(!io.TryWriteDo(12, false, out var blocked), "被拦时 TryWriteDo 返回 false");
        Check(blocked is { Kind: IoInterlockBlockKind.Blocked, Required: true, DiLevel: false },
            "被拦时详情要带规则要求值和 DI 电平");
        Check(blocked!.DiName == "Safety.TestGuard", "被拦时原因要带 DI 组件路径");

        plc.Di[2] = true;
        sensor.Tick();
        io.Tick();
        Check(!plc.Do[12] && !plc.Do[13], "Di2=true 时 SC 要把 DO12/DO13 强制为 false");

        Check(!io.WriteDo(12, true), "Di2=true 时不允许下发 DO12=true");
        Check(!io.WriteDo(13, true), "Di2=true 时不允许下发 DO13=true");
        Check(io.WriteDo(12, false), "Di2=true 时允许下发 DO12=false");
        Check(io.TryWriteDo(12, true, out var blocked2) == false
              && blocked2 is { Kind: IoInterlockBlockKind.Blocked, Required: false, DiLevel: true },
            "反向拦截也要回规则要求值");
    }

    private static void RawDiFallback(string csv)
    {
        var io = new ConfiguredIo { SafetyInterlock = "Di5=false:DO13=true" };
        io.Load();
        io.Di.Load(csv);
        io.Do.Load(csv);

        var plc = new StatePlc();
        PlcComponent.Current = plc;
        plc.Di[5] = false;

        io.Tick();
        Check(plc.Do[13], "没有 DI 组件时按 IO 点表原始电平判断，也要强制");

        Check(!io.WriteDo(13, false), "原始电平 false 时不允许下发 DO13=false");
        Check(io.WriteDo(13, true), "原始电平 false 时允许 DO13=true");

        plc.Di[5] = true;
        io.Tick();
        Check(!plc.Do[13], "原始电平 true 时要强制 DO13=false");
    }

    private static void UnknownAndConflict(string csv)
    {
        var unknown = new ConfiguredIo { SafetyInterlock = "Di99=false:DO12=true" };
        unknown.Load();
        unknown.Di.Load(csv);
        unknown.Do.Load(csv);
        var unknownPlc = new StatePlc();
        PlcComponent.Current = unknownPlc;

        Check(!unknown.WriteDo(12, false), "DI 读不到时按安全优先拦截");
        Check(unknown.TryWriteDo(12, true, out var unknownBlock) == false
              && unknownBlock is { Kind: IoInterlockBlockKind.Unknown },
            "DI 读不到时要回 unknown 详情");

        var conflict = new ConfiguredIo { SafetyInterlock = "Di3=false:DO12=true;Di4=false:DO12=false" };
        conflict.Load();
        conflict.Di.Load(csv);
        conflict.Do.Load(csv);
        var conflictPlc = new StatePlc();
        PlcComponent.Current = conflictPlc;
        conflictPlc.Di[3] = false;
        conflictPlc.Di[4] = false;

        conflict.Tick();
        Check(conflict.TryWriteDo(12, true, out var conflictBlock) == false
              && conflictBlock is { Kind: IoInterlockBlockKind.Conflict },
            "规则要求冲突时要按安全优先拦截并回 conflict 详情");
    }

    private static void SetFullPath(ComponentBase component, string fullPath)
    {
        var setter = typeof(ComponentBase).GetProperty(nameof(ComponentBase.FullPath))!
            .GetSetMethod(nonPublic: true)!;
        setter.Invoke(component, [fullPath]);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>把装配钩子和扫描拍露出来：冒烟里没有 sc.xml，直接调。</summary>
    sealed class ConfiguredIo : IoComponent
    {
        public void Load()
        {
            OnSettingLoaded(new ModuleConfig());
        }

        public void Tick()
        {
            OnScan();
        }
    }

    sealed class ScanningDi : DiSensorComponent
    {
        public void Tick() => OnScan();
    }

    /// <summary>假 PLC：DI/DO 各一张表，断了读不到也写不进。</summary>
    sealed class StatePlc : IPlc
    {
        public bool IsConnected { get; set; } = true;
        public Dictionary<int, bool> Di { get; } = [];
        public Dictionary<int, bool> Do { get; } = [];
        public void Register(string path) { }
        public bool TryReadDi(int index, out bool on) { on = IsConnected && Di.GetValueOrDefault(index); return IsConnected; }
        public bool TryReadDo(int index, out bool on) { on = IsConnected && Do.GetValueOrDefault(index); return IsConnected; }

        public bool WriteDo(int index, bool on)
        {
            if (!IsConnected)
            {
                return false;
            }

            Do[index] = on;
            return true;
        }

        public bool TryReadAi(int index, out double value) { value = 0; return false; }
        public bool TryReadAo(int index, out double value) { value = 0; return false; }
        public bool WriteAo(int index, double value) => false;
        public bool TryReadBlock(string path, out byte[] block) { block = []; return false; }
        public bool WriteBlock(string path, byte[] data) => false;
    }
}