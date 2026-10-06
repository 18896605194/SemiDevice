using xyz.Database.Eap;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// GEM 缓存（E30 Spooling）：跟 Host 断了通讯，Host 要求缓存的报文先存盘，等 Host 用 S6F23 要了再按先后发（或者清掉）。
/// 开了以后就一直开着——通讯恢复了新报文也接着进缓存（不然先后就乱了），直到缓存被取空或清掉才关。
/// 状态（开没开、开始和满了的时刻、累计条数）跟着 GEM 设定一起存盘，重启接着开着。任意线程可调。
/// </summary>
internal sealed class GemSpool
{
    private readonly object _gate = new();
    private readonly GemStore _store;
    private readonly Action _save;
    private readonly Func<int> _capacity;
    private readonly Func<bool> _overwrite;
    private bool _active;
    private DateTime? _startTime;
    private DateTime? _fullTime;
    private uint _countTotal;
    private int _countActual;

    /// <param name="store">缓存报文存哪。</param>
    /// <param name="save">状态变了要存盘（E30 把状态并进设定一起存）。</param>
    /// <param name="capacity">最多存几条（EC）。</param>
    /// <param name="overwrite">满了挤掉最早的（true）还是丢掉新来的（false）（EC）。</param>
    public GemSpool(GemStore store, Action save, Func<int> capacity, Func<bool> overwrite)
    {
        _store = store;
        _save = save;
        _capacity = capacity;
        _overwrite = overwrite;
    }

    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    /// <summary>缓存里现在有几条（SpoolCountActual）。</summary>
    public int CountActual => Volatile.Read(ref _countActual);

    /// <summary>这一轮一共进来过几条（SpoolCountTotal）。</summary>
    public uint CountTotal
    {
        get
        {
            lock (_gate)
            {
                return _countTotal;
            }
        }
    }

    public DateTime? StartTime
    {
        get
        {
            lock (_gate)
            {
                return _startTime;
            }
        }
    }

    public DateTime? FullTime
    {
        get
        {
            lock (_gate)
            {
                return _fullTime;
            }
        }
    }

    /// <summary>开机：从存着的设定里接回状态，条数按库里实际的数。</summary>
    public void Restore(GemConfig config)
    {
        lock (_gate)
        {
            _active = config.SpoolActive;
            _startTime = config.SpoolStartTime;
            _fullTime = config.SpoolFullTime;
            _countTotal = config.SpoolCountTotal;
            Volatile.Write(ref _countActual, _store.CountSpool());
        }
    }

    /// <summary>存盘前把状态写回设定（调用方拿着设定的锁）。</summary>
    public void CopyTo(GemConfig config)
    {
        lock (_gate)
        {
            config.SpoolActive = _active;
            config.SpoolStartTime = _startTime;
            config.SpoolFullTime = _fullTime;
            config.SpoolCountTotal = _countTotal;
        }
    }

    /// <summary>
    /// 断了通讯：开缓存（E30 SPOOL INACTIVE → SPOOL ACTIVE）。条数清零、记开始时刻。已经开着的不动，返回 false。
    /// </summary>
    public bool Activate()
    {
        lock (_gate)
        {
            if (_active)
            {
                return false;
            }

            _active = true;
            _startTime = DateTime.Now;
            _fullTime = null;
            _countTotal = 0;
        }

        _save();
        return true;
    }

    /// <summary>
    /// 进缓存一条（缓存开着才调）。满了按设定挤掉最早的或者丢掉这条；不管哪种，累计条数都加一。返回进没进去。
    /// </summary>
    public bool Add(SecsMessage message)
    {
        bool added;
        lock (_gate)
        {
            _countTotal++;
            int capacity = Math.Max(1, _capacity());
            bool full = CountActual >= capacity;
            if (full && _fullTime is null)
            {
                _fullTime = DateTime.Now;
            }

            if (full && !_overwrite())
            {
                added = false;
            }
            else
            {
                if (full)
                {
                    var oldest = _store.OldestSpool();
                    if (oldest is not null)
                    {
                        _store.RemoveSpool(oldest.Id);
                        Interlocked.Decrement(ref _countActual);
                    }
                }

                added = _store.AppendSpool(new GemSpoolEntity
                {
                    Stream = message.Stream,
                    Function = message.Function,
                    ReplyExpected = message.ReplyExpected,
                    Body = message.Body is null ? null : SecsCodec.Encode(message.Body),
                    CreatedAt = DateTime.Now,
                });
                if (added)
                {
                    Interlocked.Increment(ref _countActual);
                }
            }
        }

        _save();
        return added;
    }

    /// <summary>最早的一条，连同它在库里的号；空了返回 null。</summary>
    public (long Id, SecsMessage Message)? Oldest()
    {
        lock (_gate)
        {
            var row = _store.OldestSpool();
            if (row is null)
            {
                return null;
            }

            var body = row.Body is null || row.Body.Length == 0 ? null : SecsCodec.Decode(row.Body);
            return (row.Id, new SecsMessage(row.Stream, row.Function, row.ReplyExpected, body));
        }
    }

    /// <summary>删掉一条（发出去了）。</summary>
    public void Remove(long id)
    {
        lock (_gate)
        {
            _store.RemoveSpool(id);
            if (CountActual > 0)
            {
                Interlocked.Decrement(ref _countActual);
            }
        }
    }

    /// <summary>清空（Host 让清，或者取空了收尾）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _store.ClearSpool();
            Volatile.Write(ref _countActual, 0);
        }
    }

    /// <summary>缓存空了：关缓存（SPOOL ACTIVE → SPOOL INACTIVE）。原来就关着返回 false。</summary>
    public bool Deactivate()
    {
        lock (_gate)
        {
            if (!_active)
            {
                return false;
            }

            _active = false;
        }

        _save();
        return true;
    }
}
