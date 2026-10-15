using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Configs.Models;

static class VirtualIoWrites
{
    public static void Run(string directory)
    {
        var previousPlc = PlcComponent.Current;
        var previousIo = IoComponent.Current;
        try
        {
            RejectInvalidConfigurations();
            var plc = new ProbePlc();
            PlcComponent.Current = plc;
            // 用真正的 SC 装配入口验证属性匹配、父子关系和多条配置。
            var settings = new ModuleConfig
            {
                Name = "Io",
                Type = typeof(IoComponent).FullName,
                Children =
                [
                    new ModuleConfig
                    {
                        Name = "VirtualIo",
                        Type = typeof(VirtualIoComponent).FullName,
                        Values =
                        [
                            new ValueConfig { Name = "digitaloutputs", Value = "do3000=FALSE；DO3999=true" },
                            new ValueConfig { Name = "AnalogOutputs", Value = "ao5000=12.5;AO5999=-2.5" },
                            new ValueConfig { Name = "DigitalInputs", Value = "di4000=ai7>=50;DI4001=DO3000;DI4002=DI7==false;DI4003=AI6000<10;DI4004=AI6001>=50;DI4999=DO3999!=true" },
                            new ValueConfig { Name = "AnalogInputs", Value = "ai6000=AO5000;AI6001=AI7;AI6002=AO7;AI6999=AO5999" },
                        ],
                    },
                ],
            };
            var io = (IoComponent)ComponentLoader.Load([settings]).Single();
            var virtualIo = io.VirtualIo!;
            string csv = Path.Combine(directory, "virtual-source.csv");
            File.WriteAllText(csv, "Index,Name,PhysicalMin,PhysicalMax,LogicalMin,LogicalMax\n7,Source,0,1000,0,100\n");
            foreach (var table in new[] { io.Di, io.Do, io.Ai, io.Ao })
            {
                table.Load(csv);
            }

            Check(io.TryReadDo(3000, out bool level) && !level, "virtual DO initial value");
            Check(io.WriteDo(3000, true) && io.TryReadDi(4001, out level) && level, "software DO drives mapped DI immediately");
            Check(io.TryReadDi(4000, out level) && level, "AI engineering value comparison");
            Check(io.TryReadDi(4002, out level) && level, "DI false comparison");
            Check(io.TryReadAi(6001, out double value) && value == 65, "virtual AI preserves engineering scaling exactly once");
            Check(io.TryReadAi(6002, out value) && value == 20, "physical AO mapping uses engineering value");
            Check(io.TryReadAi(6000, out value) && value == 12.5, "virtual AO to AI");
            Check(io.WriteAo(5000, 7.5) && io.TryReadDi(4003, out level) && level, "AO to AI to DI dependency chain");
            Check(io.TryReadAi(6999, out value) && value == -2.5, "last AI/AO indices and negative values");
            Check(io.TryReadDi(4999, out level) && !level, "last DI/DO indices and boolean inequality");
            Check(!io.WriteAo(5000, double.NaN) && !io.WriteAo(5000, double.PositiveInfinity)
                && io.TryReadAo(5000, out value) && value == 7.5, "invalid numeric writes do not change output");

            foreach (int index in new[] { 3000, 3999, 4000, 4999, 5000, 5999, 6000, 6999, 7000 })
            {
                if (!VirtualIoComponent.IsIndexInRange("DI", index))
                {
                    Check(!io.TryReadDi(index, out _), "wrong DI range must not fall through to PLC");
                }

                if (!VirtualIoComponent.IsIndexInRange("DO", index))
                {
                    Check(!io.WriteDo(index, false), "wrong DO range must not fall through to PLC");
                }

                if (!VirtualIoComponent.IsIndexInRange("AO", index))
                {
                    Check(!io.WriteAo(index, 1), "wrong AO range must not fall through to PLC");
                }

                if (!VirtualIoComponent.IsIndexInRange("AI", index))
                {
                    Check(!io.TryReadAi(index, out _), "wrong AI range must not fall through to PLC");
                }
            }

            Check(!io.TryReadDi(4005, out _) && !io.WriteDo(3001, true) && !io.WriteAo(5001, 5), "undeclared points fail");
            plc.AiValue = double.NaN;
            Check(!io.TryReadAi(6001, out _) && !io.TryReadDi(4000, out _), "non-finite source invalidates inputs");
            plc.AiValue = 650;
            plc.IsConnected = false;
            Check(!io.TryReadDi(4000, out _) && !io.TryReadAi(6001, out _), "disconnect invalidates physical dependencies before next scan");
            Check(virtualIo.DigitalInputValues[4000] is null && virtualIo.AnalogInputValues[6001] is null,
                "invalid is distinct from false/zero in snapshots");
            Check(io.TryReadDi(4003, out _) && io.TryReadAi(6000, out _), "software-only dependencies work without PLC");
            plc.IsConnected = true;
            plc.AiValue = 400;
            Check(io.TryReadDi(4000, out level) && !level, "reconnect recalculates source instead of retaining stale state");
            Check(plc.Indices.All(index => index < 3000) && plc.Writes == 0, "virtual IO never reaches PLC write/read indices");
            Check(io.WriteDo(7, true) && plc.Writes == 1, "physical output still reaches PLC");

            // 未安装虚拟组件时，保留区间也不得落入 PLC；即使物理 CSV 意外列出该索引。
            var plain = new IoComponent();
            string reservedCsv = Path.Combine(directory, "virtual-reserved.csv");
            File.WriteAllText(reservedCsv, "Index,Name\n3000,Reserved\n4000,Reserved\n5000,Reserved\n6000,Reserved\n");
            foreach (var table in new[] { plain.Di, plain.Do, plain.Ai, plain.Ao })
            {
                table.Load(reservedCsv);
            }

            Check(!plain.WriteDo(3000, true) && !plain.WriteAo(5000, 1)
                && !plain.TryReadDi(4000, out _) && !plain.TryReadAi(6000, out _), "missing virtual component fails closed");
            Check(plc.Indices.All(index => index < 3000) && plc.Writes == 1, "reserved points never leak into physical PLC");
            StandaloneComponent();
            Console.WriteLine("PASS: virtual IO configuration, ranges, comparisons, mappings, validity, engineering values and PLC isolation");
        }
        finally
        {
            PlcComponent.Current = previousPlc;
            IoComponent.Current = previousIo;
        }
    }

    private static void StandaloneComponent()
    {
        var roots = ComponentLoader.Load(
        [
            new ModuleConfig { Name = "Io", Type = typeof(IoComponent).FullName },
            new ModuleConfig
            {
                Name = "VirtualIo",
                Type = typeof(VirtualIoComponent).FullName,
                Values =
                [
                    new ValueConfig { Name = "IsRun", Value = "true" },
                    new ValueConfig { Name = "DigitalOutputs", Value = "DO3000=true" },
                    new ValueConfig { Name = "AnalogOutputs", Value = "AO5000=5" },
                    new ValueConfig { Name = "AnalogInputs", Value = "AI6000=AO5000" },
                    new ValueConfig { Name = "DigitalInputs", Value = "DI4000=AI6000>5;DI4001=AI6000>=5;DI4002=AI6000<5;DI4003=AI6000<=5;DI4004=AI6000==5;DI4005=AI6000!=5" },
                ],
            },
        ]);
        var io = roots.OfType<IoComponent>().Single();
        var component = roots.OfType<VirtualIoComponent>().Single();
        Check(ReferenceEquals(io.VirtualIo, component) && io.Children.Count == 0,
            "standalone virtual root automatically binds without becoming an IO child");
        Check(component.DigitalInputValues.Values.All(value => value is null), "input snapshots start invalid before evaluation");
        ComponentLoader.StartScanning(roots);
        Check(SpinWait.SpinUntil(() => component.DigitalInputValues.Values.All(value => value.HasValue), 3000),
            "standalone IsRun root updates snapshots on its own scan thread");
        bool[] expected = [false, true, false, true, true, false];
        for (int i = 0; i < expected.Length; i++)
        {
            Check(io.TryReadDi(4000 + i, out bool level) && level == expected[i], "numeric comparison boundary");
        }

        Check(io.WriteAo(5000, 8) && io.TryReadDi(4000, out bool on) && on,
            "standalone IO routing observes updated software outputs");

        var softwareOnly = (VirtualIoComponent)ComponentLoader.Load(
        [
            new ModuleConfig
            {
                Name = "SoftwareOnly",
                Type = typeof(VirtualIoComponent).FullName,
                Values = [new ValueConfig { Name = "DigitalOutputs", Value = "DO3000=true" }],
            },
        ]).Single();
        Check(softwareOnly.TryReadDo(3000, out on) && on, "pure software component needs no physical IO owner");

        bool rejected = false;
        try
        {
            ComponentLoader.Load(
            [
                new ModuleConfig
                {
                    Name = "MissingPhysicalIo",
                    Type = typeof(VirtualIoComponent).FullName,
                    Values = [new ValueConfig { Name = "DigitalInputs", Value = "DI4000=AI7>0" }],
                },
            ]);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Check(rejected, "physical source requires a physical IO owner");
    }

    private static void RejectInvalidConfigurations()
    {
        foreach (var component in new[]
        {
            new ConfiguredVirtual { DigitalOutputs = "DO2999=true" },
            new ConfiguredVirtual { DigitalOutputs = "DO4000=true" },
            new ConfiguredVirtual { DigitalInputs = "DI5000=AI7>0" },
            new ConfiguredVirtual { AnalogOutputs = "AO6000=0" },
            new ConfiguredVirtual { AnalogInputs = "AI7000=AI7" },
            new ConfiguredVirtual { DigitalOutputs = "DO3000=true;DO3000=false" },
            new ConfiguredVirtual { DigitalOutputs = "DO3000=1" },
            new ConfiguredVirtual { AnalogOutputs = "AO5000=NaN" },
            new ConfiguredVirtual { AnalogInputs = "AI6000=DI7" },
            new ConfiguredVirtual { DigitalInputs = "DI4000=AI7" },
            new ConfiguredVirtual { DigitalInputs = "DI4000=AI7>NaN" },
            new ConfiguredVirtual { DigitalInputs = "DI4000=AI7>=50;DI4000=AI7<5" },
            new ConfiguredVirtual { DigitalInputs = "DI4000=DI7>0" },
            new ConfiguredVirtual { DigitalInputs = "DI4000=DO3001" },
            new ConfiguredVirtual { DigitalInputs = "DI4000=DI4001;DI4001=DI4000" },
            new ConfiguredVirtual { AnalogInputs = "AI6000=AI6001;AI6001=AI6000" },
            new ConfiguredVirtual { DigitalInputs = "DI4000=DO5000" },
            new ConfiguredVirtual { DigitalInputs = "DI4000=AI999999999999>5" },
        })
        {
            bool rejected = false;
            try
            {
                component.Load();
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            Check(rejected, "invalid virtual IO config must fail during assembly");
        }

        var empty = new ConfiguredVirtual();
        empty.Load();
        Check(!empty.TryReadDi(4000, out _) && !empty.WriteDo(3000, true), "empty config adds no points");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ConfiguredVirtual : VirtualIoComponent
    {
        public void Load() => OnSettingLoaded(new ModuleConfig());
    }

    private sealed class ProbePlc : IPlc
    {
        public bool IsConnected { get; set; } = true;
        public double AiValue { get; set; } = 650;
        public List<int> Indices { get; } = [];
        public int Writes { get; private set; }

        public bool TryReadDi(int index, out bool on)
        {
            Indices.Add(index);
            on = false;
            return IsConnected;
        }

        public bool TryReadDo(int index, out bool on)
        {
            Indices.Add(index);
            on = true;
            return IsConnected;
        }

        public bool TryReadAi(int index, out double value)
        {
            Indices.Add(index);
            value = AiValue;
            return IsConnected;
        }

        public bool TryReadAo(int index, out double value)
        {
            Indices.Add(index);
            value = 200;
            return IsConnected;
        }

        public bool WriteDo(int index, bool on)
        {
            Indices.Add(index);
            Writes++;
            return IsConnected;
        }

        public bool WriteAo(int index, double value)
        {
            Indices.Add(index);
            Writes++;
            return IsConnected;
        }

        public void Register(string path)
        {
        }

        public bool TryReadBlock(string path, out byte[] block)
        {
            block = [];
            return false;
        }

        public bool WriteBlock(string path, byte[] data) => false;
    }
}
