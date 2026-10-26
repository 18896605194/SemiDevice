using System.Collections.Concurrent;
using System.Globalization;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Configs.Models;
using xyz.Tools;

static class VirtualInputReads
{
    public static void Run(string directory)
    {
        var previousPlc = IocHelper.GetService<IPlcIo>();
        var previousIo = IocHelper.GetService<IIo>();
        try
        {
            InvalidConfigurations();
            var plc = new ProbePlc();
            SmokeIoc.Use<IPlcIo>(plc);
            var roots = SmokeIoc.Load(
            [
                new ModuleConfig { Name = "Io", Type = typeof(IoComponent).FullName },
                Group("ModuleA", Point<VirtualDiComponent>(4000, "ai6001 > 50 && DI12 && !DO12")),
                Group("ModuleB", Group("Sensors", Point<VirtualAiComponent>(6001, "(AI5+AI6)/2"))),
                Point<VirtualAiComponent>(6000, "(AI5+AI5)/2"),
                Point<VirtualAiComponent>(6002, "AO5 + AI6001 * 2"),
                Point<VirtualAiComponent>(6003, "AI5 / (AI6-AI6)"),
                Point<VirtualAiComponent>(6004, "1e308 * 1e308"),
                Point<VirtualDiComponent>(4001, "false && AI6003 > 1"),
                Point<VirtualDiComponent>(4999, "DI12 == true || DO12 != false"),
                Point<VirtualAiComponent>(6999, "-2 + +3 * (4 - 1) / 2"),
            ]);
            var io = roots.OfType<IoComponent>().Single();
            var moduleA = (VirtualInputGroup)roots.Single(root => root.Name == "ModuleA");
            var moduleB = (VirtualInputGroup)roots.Single(root => root.Name == "ModuleB");
            var ready = moduleA.FindChild<VirtualDiComponent>()!;
            var average = moduleB.FindChild<VirtualAiComponent>()!;
            // 点表放进 IO 组件的点表目录：它开机准备时读点表、取 PLC
            string tables = Path.Combine(directory, "virtual-io");
            Directory.CreateDirectory(tables);
            foreach (string table in new[] { "DI", "DO", "AI", "AO" })
            {
                File.WriteAllText(Path.Combine(tables, table + ".csv"),
                    "Index,Name,PhysicalMin,PhysicalMax,LogicalMin,LogicalMax\n5,A,0,1000,0,100\n6,B,0,1000,0,100\n12,Signal,0,1000,0,100\n");
            }

            io.PointTableDirectory = tables;
            io.InitComponent();

            Check(ready.Value is null && average.Value is null && !ready.IsValid, "components start invalid");
            moduleA.Tick();
            Check(ready.Value == true && average.Value == 60 && ready.IsValid,
                "parent scan computes cross-module dependencies even if source module has not scanned");
            Check(ready.FullPath == "ModuleA.P4000" && average.FullPath == "ModuleB.Sensors.P6001",
                "computed components retain ownership paths at arbitrary depth");
            Check(io.TryReadDi(4000, out bool on) && on && io.TryReadAi(6001, out double value) && value == 60,
                "global index routing reads nested components");
            Check(average.TryRead(out value) && value == 60 && ready.TryRead(out on) && on,
                "typed component read APIs work independently of parent path");
            Check(io.TryReadAi(6002, out value) && value == 130, "engineering values, arithmetic precedence and physical AO input");
            Check(io.TryReadAi(6999, out value) && value == 2.5 && io.TryReadDi(4999, out on) && on,
                "inclusive last indices, unary operators, parentheses and boolean comparisons");
            Check(!io.TryReadAi(6003, out _) && !io.TryReadAi(6004, out _), "division by zero and overflow are invalid");
            Check(!io.TryReadDi(4001, out _), "logical expressions propagate invalid sources rather than hiding them");
            int previousReads = plc.ReadCounts.GetValueOrDefault(5);
            Check(io.TryReadAi(6000, out value) && value == 80 && plc.ReadCounts[5] == previousReads + 1,
                "each source is sampled once per evaluation");

            plc.Ai5 = 400;
            moduleA.Tick();
            Check(ready.Value == false && average.Value == 40, "dependency update is independent of scan order");
            plc.Connected = false;
            Check(!io.TryReadDi(4000, out _) && ready.Value is null && average.Value is null && !average.IsValid,
                "disconnect invalidates dependent snapshots immediately on read");
            Check(io.TryReadAi(6999, out value) && value == 2.5, "constant virtual expressions do not depend on PLC connection");
            plc.Connected = true;
            plc.Ai5 = double.NaN;
            Check(!average.TryRead(out _), "non-finite physical source is invalid");
            plc.Ai5 = 800;
            Check(ready.TryRead(out on) && on, "reconnect/source recovery recalculates values");

            Parallel.For(0, 100, _ =>
            {
                Check(io.TryReadAi(6001, out double number) && number == 60, "concurrent cross-module reads");
            });

            foreach (int index in new[] { 3000, 3999, 4000, 4999, 5000, 5999, 6000, 6999, 7000 })
            {
                Check(!io.WriteDo(index, true).IsSuccess && !io.WriteAo(index, 1).IsSuccess
                    && !io.TryReadDo(index, out _) && !io.TryReadAo(index, out _), "virtual outputs are removed and never sent to PLC");
                if (index < 4000 || index >= 5000)
                {
                    Check(!io.TryReadDi(index, out _), "DI rejects mismatched reserved ranges");
                }

                if (index < 6000 || index >= 7000)
                {
                    Check(!io.TryReadAi(index, out _), "AI rejects mismatched reserved ranges");
                }
            }

            Check(!io.TryReadDi(4010, out _) && !io.TryReadAi(6010, out _), "undeclared virtual inputs remain invalid");
            Check(plc.Indices.All(index => index < 3000) && plc.Writes == 0, "no virtual index reaches PLC");
            Check(io.WriteDo(12, true).IsSuccess && plc.Writes == 1, "physical writes remain unchanged");

            CheckStandaloneAndOperators();
            Check(io.TryReadAi(6001, out value) && value == 60, "another assembly does not replace existing registry");
            CheckMissingPhysicalPoint(directory);
            Console.WriteLine("PASS: virtual DI/AI components, nested ownership, arithmetic/logic, dependencies, validation, validity and PLC isolation");
        }
        finally
        {
            SmokeIoc.Use<IPlcIo>(previousPlc);
            SmokeIoc.Use<IIo>(previousIo);
        }
    }

    private static void CheckStandaloneAndOperators()
    {
        var expressions = new (string Text, bool Expected)[]
        {
            ("1+2*3==7", true), ("8/4/2==1", true), ("5-2-1==2", true),
            ("5>5", false), ("5>=5", true), ("5<5", false), ("5<=5", true),
            ("5!=5", false), ("TRUE && !false", true), ("false || true && false", false),
            ("(false || true) && true", true), ("(1e2 + .5) == 100.5", true),
        };
        var settings = expressions.Select((item, index) => Point<VirtualDiComponent>(4000 + index, item.Text)).ToList();
        settings.Add(Point<VirtualAiComponent>(6001, "6/2", run: true));
        var roots = SmokeIoc.Load(settings);
        foreach (var (item, index) in expressions.Select((item, index) => (item, index)))
        {
            Check(((VirtualDiComponent)roots[index]).TryRead(out bool value) && value == item.Expected, $"expression: {item.Text}");
        }

        var ai = roots.OfType<VirtualAiComponent>().Single();
        ComponentLoader.StartScanning(roots);
        Check(SpinWait.SpinUntil(() => ai.Value == 3, 3000), "standalone virtual point scans with IsRun=true");
    }

    private static void CheckMissingPhysicalPoint(string directory)
    {
        string configDirectory = Path.Combine(directory, "virtual-validation");
        Directory.CreateDirectory(configDirectory);
        var roots = SmokeIoc.Load(
        [
            new ModuleConfig { Name = "Io", Type = typeof(IoComponent).FullName },
            Point<VirtualAiComponent>(6001, "AI999+1"),
        ]);
        var io = roots.OfType<IoComponent>().Single();
        io.PointTableDirectory = configDirectory;
        bool rejected = false;
        try
        {
            io.Open();
        }
        catch (InvalidOperationException exception)
        {
            rejected = exception.Message.Contains("AI999");
        }

        Check(rejected, "missing physical sources fail after point-table loading");
    }

    private static void InvalidConfigurations()
    {
        foreach (string expression in new[] { "", "AI5+", "(AI5+2", "AI5)", "AI5**2", "AI5=2", "NaN", "1e999", "true+2", "!AI5", "DI12*2", "AI7000", "DO3000", "AO5000", "AI99999999999", "AI5;AI6", "AI5 & AI6" })
        {
            Reject([PhysicalIo(), Point<VirtualAiComponent>(6001, expression)], $"bad expression {expression}");
        }

        Reject([Point<VirtualAiComponent>(6001, "true")], "AI cannot return bool");
        Reject([Point<VirtualDiComponent>(4000, "1")], "DI cannot return number");
        Reject([Point<VirtualDiComponent>(4000, "true==1")], "equality operands require matching types");
        Reject([Point<VirtualDiComponent>(4000, "true && 1")], "logical operands require bool");
        Reject([Point<VirtualDiComponent>(3999, "true")], "DI lower bound");
        Reject([Point<VirtualDiComponent>(5000, "true")], "DI upper bound");
        Reject([Point<VirtualAiComponent>(5999, "1")], "AI lower bound");
        Reject([Point<VirtualAiComponent>(7000, "1")], "AI upper bound");
        Reject([Group("A", Point<VirtualDiComponent>(4000, "true")), Group("B", Point<VirtualDiComponent>(4000, "false"))], "global duplicate DI");
        Reject([Point<VirtualAiComponent>(6001, "1"), Group("B", Point<VirtualAiComponent>(6001, "2"))], "global duplicate AI");
        Reject([Point<VirtualAiComponent>(6001, "AI6002")], "missing virtual source");
        Reject([Point<VirtualDiComponent>(4000, "DI4000")], "self cycle");
        Reject([Point<VirtualAiComponent>(6001, "AI6002"), Point<VirtualAiComponent>(6002, "AI6001")], "dependency cycle");
        Reject([Point<VirtualAiComponent>(6001, "AI5")], "physical source requires IO owner");
        Reject([Point<VirtualAiComponent>(6001, new string('(', 150) + "1" + new string(')', 150))], "nesting limit");
        Reject([Point<VirtualAiComponent>(6001, new string('1', 4097))], "expression length limit");
    }

    private static void Reject(List<ModuleConfig> settings, string message)
    {
        bool rejected = false;
        try
        {
            SmokeIoc.Load(settings);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Check(rejected, message);
    }

    private static ModuleConfig PhysicalIo() => new() { Name = "Io", Type = typeof(IoComponent).FullName };

    private static ModuleConfig Group(string name, params ModuleConfig[] children) => new()
    {
        Name = name,
        Type = typeof(VirtualInputGroup).FullName,
        Children = children.ToList(),
    };

    private static ModuleConfig Point<T>(int index, string expression, bool run = false) => new()
    {
        Name = $"P{index}",
        Type = typeof(T).FullName,
        Values =
        [
            new ValueConfig { Name = "index", Value = index.ToString(CultureInfo.InvariantCulture) },
            new ValueConfig { Name = "Expression", Value = expression },
            new ValueConfig { Name = "IsRun", Value = run.ToString() },
        ],
    };

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ProbePlc : IPlcIo
    {
        public bool Connected { get; set; } = true;
        public bool IsConnected => Connected;
        public double Ai5 { get; set; } = 800;
        public ConcurrentBag<int> Indices { get; } = [];
        public ConcurrentDictionary<int, int> ReadCounts { get; } = new();
        public int Writes { get; private set; }

        public bool TryReadDi(int index, out bool on)
        {
            Indices.Add(index);
            on = true;
            return IsConnected;
        }

        public bool TryReadDo(int index, out bool on)
        {
            Indices.Add(index);
            on = false;
            return IsConnected;
        }

        public bool TryReadAi(int index, out double value)
        {
            Indices.Add(index);
            ReadCounts.AddOrUpdate(index, 1, (_, count) => count + 1);
            value = index == 5 ? Ai5 : 400;
            return IsConnected;
        }

        public bool TryReadAo(int index, out double value)
        {
            Indices.Add(index);
            value = 100;
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
    }
}

[Component]
public sealed class VirtualInputGroup : ComponentBase
{
    public void Tick() => OnScan();
}
