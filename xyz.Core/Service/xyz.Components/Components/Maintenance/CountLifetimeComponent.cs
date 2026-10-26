using System.Threading.Channels;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Database.DbProvider;
using xyz.Database.Lifetime;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// 部件寿命（次数）：挂在要管寿命的部件下面
/// </summary>
[Component(description: "部件寿命（次数）组件（盯一个 DO，断→通、通→断各算一次；到 EC 警告次数报警告、到报警次数报报警）")]
public class CountLifetimeComponent : ComponentBase, ICountLifetime
{
    #region Machine

    [Machine("-1", "IO", "盯的 DO 索引（真 DO，或虚拟 IO 里的虚拟 DO 5000~5999）：每变一次（断→通、通→断）算用了一次", Required = true)]
    public int DoIndex { get; set; } = -1;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "次", "0", "2147483647", "0",
        "警告次数：已用次数到了报寿命快到期警告；0 = 不管")]
    public int WarnCount
    {
        get { return GetEcInt(nameof(WarnCount)); }
        set { SetEcInt(nameof(WarnCount), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "次", "0", "2147483647", "0",
        "报警次数：已用次数到了报寿命到期报警；0 = 不管")]
    public int AlarmCount
    {
        get { return GetEcInt(nameof(AlarmCount)); }
        set { SetEcInt(nameof(AlarmCount), value); }
    }

    #endregion

    #region Alarm

    [Alarm("寿命快到期", AlarmCategory.EquipmentStatusWarning, AlarmLevel = AlarmLevel.Warn,
        Description = "这个部件的已用次数到了 EC 设的警告次数",
        Solution = "准备备件、安排换件；换完把已用次数清零再复位报警")]
    public string WarnAlarm = nameof(WarnAlarm);

    [Alarm("寿命到期", AlarmCategory.EquipmentStatusWarning, AlarmLevel = AlarmLevel.Alarm1,
        Description = "这个部件的已用次数到了 EC 设的报警次数",
        Solution = "换件，换完把已用次数清零再复位报警；暂时不换就把 EC AlarmCount 调高")]
    public string ExpiredAlarm = nameof(ExpiredAlarm);

    #endregion

    #region 次数

    private readonly object _gate = new();

    private long _count;

    /// <summary>
    /// 开机从库里读过了（只读一次）；读过以后才往库里写
    /// </summary>
    private bool _loaded;

    /// <summary>
    /// 库里这一行的键（开机读库时取的全路径）
    /// </summary>
    private string _path = string.Empty;

    /// <summary>
    /// 上一拍读到的 DO；读不到是 null，重新读到的那一拍只记下不算
    /// </summary>
    private bool? _lastLevel;

    /// <summary>
    /// 一次写库：加一，或者清零
    /// </summary>
    private readonly record struct CountWrite(bool Clear, DateTime At);

    /// <summary>
    /// 后台写库的队列：一个读者按先后写
    /// </summary>
    private readonly Channel<CountWrite> _writes =
        Channel.CreateUnbounded<CountWrite>(new UnboundedChannelOptions { SingleReader = true });

    public long UsedCount
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    public void Clear()
    {
        long before;
        bool loaded;
        lock (_gate)
        {
            before = _count;
            _count = 0;
            loaded = _loaded;
        }

        if (loaded)
        {
            _writes.Writer.TryWrite(new CountWrite(true, DateTime.Now));
        }

        LogHelper.Info(this, $"已用次数清零（换件），清零前 {before} 次");
    }

    #endregion

    private IIo? _io;

    private IVirtualIo? _virtualIo;

    public override void Bind()
    {
        _io = IocHelper.GetService<IIo>();
        _virtualIo = IocHelper.GetService<IVirtualIo>();
        Load();
    }

    #region 扫描

    protected override void OnScan()
    {
        base.OnScan();
        bool? level = ReadDo();
        if (level is not null && _lastLevel is not null && level.Value != _lastLevel.Value)
        {
            AddOne();
        }

        _lastLevel = level;
        long count = UsedCount;
        int alarmCount = AlarmCount;
        if (alarmCount > 0 && count >= alarmCount)
        {
            RaiseAlarm(ExpiredAlarm);
            return;
        }

        int warnCount = WarnCount;
        if (warnCount > 0 && count >= warnCount)
        {
            RaiseAlarm(WarnAlarm);
        }
    }

    /// <summary>
    /// 读 DO（true = 通）：虚拟 IO 里有这个点号就读它，没有再读真 DO；都读不到返回 null。探针测试重写它直接摆读数
    /// </summary>
    protected virtual bool? ReadDo()
    {
        if (DoIndex < 0)
        {
            return null;
        }

        bool on;
        if (_virtualIo is not null && _virtualIo.TryReadDo(DoIndex, out on))
        {
            return on;
        }

        if (_io is null || !_io.TryReadDo(DoIndex, out on))
        {
            return null;
        }

        return on;
    }

    #endregion

    #region 存库

    /// <summary>
    /// 开机读回已用次数接着数；读不了只记日志，这次从 0 数（库里照样加）
    /// </summary>
    private void Load()
    {
        string path;
        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _path = FullPath;
            path = _path;
        }

        try
        {
            using var db = XyzDb.Create();
            db.CodeFirst.InitTables<CountLifetimeEntity>();
            var row = db.Queryable<CountLifetimeEntity>().First(item => item.Path == path);
            if (row is not null)
            {
                lock (_gate)
                {
                    _count += row.UsedCount;
                }
            }
        }
        catch (Exception exception)
        {
            LogHelper.Error(this, $"读已用次数失败，这次从 0 数（库里照样加）：{exception.Message}");
        }

        _ = Task.Run(WriteLoopAsync);
    }

    /// <summary>
    /// 用了一次：内存加一，交给后台给库里那行加一
    /// </summary>
    private void AddOne()
    {
        bool loaded;
        lock (_gate)
        {
            _count++;
            loaded = _loaded;
        }

        if (loaded)
        {
            _writes.Writer.TryWrite(new CountWrite(false, DateTime.Now));
        }
    }

    /// <summary>
    /// 后台写库
    /// </summary>
    private async Task WriteLoopAsync()
    {
        await foreach (var write in _writes.Reader.ReadAllAsync())
        {
            try
            {
                using var db = XyzDb.Create();
                if (write.Clear)
                {
                    db.Updateable<CountLifetimeEntity>()
                        .SetColumns(item => new CountLifetimeEntity { UsedCount = 0, UpdatedAt = write.At })
                        .Where(item => item.Path == _path)
                        .ExecuteCommand();
                }
                else
                {
                    int updated = db.Updateable<CountLifetimeEntity>()
                        .SetColumns(item => new CountLifetimeEntity { UsedCount = item.UsedCount + 1, UpdatedAt = write.At })
                        .Where(item => item.Path == _path)
                        .ExecuteCommand();
                    if (updated == 0)
                    {
                        db.Insertable(new CountLifetimeEntity { Path = _path, UsedCount = 1, UpdatedAt = write.At }).ExecuteCommand();
                    }
                }
            }
            catch (Exception exception)
            {
                LogHelper.Error(this, $"写已用次数失败：{exception.Message}");
            }
        }
    }

    #endregion
}
