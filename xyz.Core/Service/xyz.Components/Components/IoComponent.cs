using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Io;
using xyz.Configs;

namespace xyz.Components.Components;


[Component(description: "IO 组件（点表 + 持续采集）")]
public class IoComponent : ComponentBase
{
    /// <summary>
    /// 当前 IO 表；sc.xml 里装出来即生效，上层可按索引取点。
    /// </summary>
    public static IoComponent? Current { get; set; }

    public IoComponent()
    {
        Current = this;
    }

    #region SC

    [SCEditor("IO", "Io", "点表目录（相对 sc.xml 所在目录），里面放 DI.csv / DO.csv / AI.csv / AO.csv")]
    public string PointTableDirectory { get; set; } = "IO";

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "100", "10000", "500", "IO 界面推送周期（采集照常每拍走，这个只管推给界面的快慢）")]
    public int PublishIntervalMs
    {
        get { return GetEcInt(nameof(PublishIntervalMs)); }
        set { SetEcInt(nameof(PublishIntervalMs), value); }
    }

    #endregion

    #region SV

    /// <summary>
    /// 采集是否在工作（SV）：PLC 连着才算。断了以后所有点的 IsValid 都会翻成 false。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "IO 采集是否在工作")]
    public bool IsCollecting { get; private set; }

    #endregion

    #region 点表

    public IoPointTable Di { get; } = new("DI");

    public IoPointTable Do { get; } = new("DO");

    public IoPointTable Ai { get; } = new("AI");

    public IoPointTable Ao { get; } = new("AO");

    #endregion

    #region 装载与采集

    private readonly object _collectionGate = new();
    private CancellationTokenSource? _collectionCancellation;
    private Task? _collectionTask;

    /// <summary>
    /// 读点表；由装配在 StartCollecting 之前调用。点表不在就是空表，照常空转不拦启动——
    /// 装机时电控还没给点表是常事。
    /// </summary>
    public bool Open()
    {
        // 全限定：组件基类自己有个 Path 属性（组件路径），这儿的 Path 不是它。
        string directory = System.IO.Path.Combine(SC.ConfigDirectory, PointTableDirectory);

        Di.Load(System.IO.Path.Combine(directory, "DI.csv"));
        Do.Load(System.IO.Path.Combine(directory, "DO.csv"));
        Ai.Load(System.IO.Path.Combine(directory, "AI.csv"));
        Ao.Load(System.IO.Path.Combine(directory, "AO.csv"));

        LogHelper.Info($"[{FullPath}] 点表装载：DI {Di.Points.Count}、DO {Do.Points.Count}、"
                       + $"AI {Ai.Points.Count}、AO {Ao.Points.Count} 点（{directory}）");
        return true;
    }

    /// <summary>
    /// 启动 IO 专用长任务，不依赖组件树的 OnScan。重复启动不创建新线程。
    /// </summary>
    public void StartCollecting()
    {
        lock (_collectionGate)
        {
            if (_collectionTask is not null)
            {
                return;
            }

            _collectionCancellation = new CancellationTokenSource();
            var token = _collectionCancellation.Token;
            _collectionTask = Task.Factory.StartNew( () => CollectionLoop(token), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    /// <summary>停止并等待采集退出；停止后各点不再视为有效。</summary>
    public void StopCollecting()
    {
        lock (_collectionGate)
        {
            if (_collectionTask is null)
            {
                return;
            }

            _collectionCancellation!.Cancel();
            try
            {
                _collectionTask.GetAwaiter().GetResult();
            }
            finally
            {
                _collectionCancellation.Dispose();
                _collectionCancellation = null;
                _collectionTask = null;
            }
        }
    }

    private void CollectionLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    Collect();
                }
                catch (Exception exception)
                {
                    IsCollecting = false;
                    Invalidate();
                    LogHelper.Warn("Io", $"[{FullPath}] IO 采集异常: {exception.Message}");
                }

                // 本轮采集完成后等 50ms；停止时立即唤醒。
                if (token.WaitHandle.WaitOne(50))
                {
                    break;
                }
            }
        }
        finally
        {
            IsCollecting = false;
            Invalidate();
        }
    }

    private void Collect()
    {
        var plc = PlcComponent.Current;
        IsCollecting = plc is not null && plc.IsConnected;
        if (plc is null || !IsCollecting)
        {
            // PLC 断了：所有点作废。留着上一拍的值不标记，上层会把陈旧值当真。
            Invalidate();
            return;
        }

        foreach (var point in Di.Points)
        {
            point.IsValid = plc.TryReadDi(point.Index, out bool on);
            point.IsOn = on;
        }

        foreach (var point in Do.Points)
        {
            point.IsValid = plc.TryReadDo(point.Index, out bool on);
            point.IsOn = on;
        }

        foreach (var point in Ai.Points)
        {
            point.IsValid = plc.TryReadAi(point.Index, out double raw);
            point.Raw = raw;
        }

        foreach (var point in Ao.Points)
        {
            point.IsValid = plc.TryReadAo(point.Index, out double raw);
            point.Raw = raw;
        }
    }

    private void Invalidate()
    {
        foreach (var table in new[] { Di, Do, Ai, Ao })
        {
            foreach (var point in table.Points)
            {
                point.IsValid = false;
            }
        }
    }

    #endregion

    #region 按索引读写

    /// <summary>
    /// 读一个 DI 点。点表里没有该索引或点位无效时返回 false。
    /// </summary>
    public bool TryReadDi(int index, out bool on)
    {
        on = false;
        var point = Di.Find(index);
        if (point is null || !point.IsValid)
        {
            return false;
        }

        on = point.IsOn;
        return true;
    }

    /// <summary>
    /// 回读一个 DO 点当前的输出状态。
    /// </summary>
    public bool TryReadDo(int index, out bool on)
    {
        on = false;
        var point = Do.Find(index);
        if (point is null || !point.IsValid)
        {
            return false;
        }

        on = point.IsOn;
        return true;
    }

    /// <summary>
    /// 按索引写一个 DO 点，点表里没有该索引时返回 false。
    /// </summary>
    public bool WriteDo(int index, bool on)
    {
        var point = Do.Find(index);
        var plc = PlcComponent.Current;
        if (point is null || plc is null)
        {
            return false;
        }

        return plc.WriteDo(point.Index, on);
    }

    /// <summary>
    /// 读一个 AI 点的工程值（按点表里的标定换算；没标定就是原始码）。
    /// </summary>
    public bool TryReadAi(int index, out double value)
    {
        value = 0;
        var point = Ai.Find(index);
        if (point is null || !point.IsValid)
        {
            return false;
        }

        value = point.Value;
        return true;
    }

    /// <summary>
    /// 写一个 AO 点：给的是工程值，按标定反算成原始码再下发。
    /// </summary>
    public bool WriteAo(int index, double engineering)
    {
        var point = Ao.Find(index);
        var plc = PlcComponent.Current;
        if (point is null || plc is null)
        {
            return false;
        }

        return plc.WriteAo(point.Index, point.ToRaw(engineering));
    }

    #endregion
}
