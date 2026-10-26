using System.Diagnostics;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// 心跳组件
/// </summary>
[Component(description: "心跳组件（按 machine.xml 配的时间轮流给一个 DO 写通、断，PLC 看它判上位机在不在）")]
public class HeartbeatComponent : ComponentBase, IHeartbeat
{
    #region Machine

    [Machine("-1", "IO", "心跳 DO 索引：按下面的时间轮流写通、断；-1 = 不发", Required = true)]
    public int DoIndex { get; set; } = -1;

    [Machine("1000", "Config", "隔多久翻一次（毫秒）：1000 = 1 s 写一次通、1 s 写一次断；跟 PLC 那边判心跳的时间对好")]
    public int IntervalMs { get; set; } = 1000;

    #endregion

    #region Alarm

    [Alarm("心跳发送失败", AlarmCategory.IrrecoverableError, AlarmLevel = AlarmLevel.Alarm1,
        Description = "心跳 DO 没写进 PLC；一直写不进去，PLC 那边看心跳停了会当上位机掉线",
        Solution = "检查 PLC 通讯、DO 点表里有没有 machine.xml 配的这个 DoIndex、IO 联锁是不是锁住了这个 DO；恢复后复位清警")]
    public string SendFailedAlarm = nameof(SendFailedAlarm);

    #endregion

    #region 状态

    /// <summary>
    /// 最近一次写成的值；下一次写它的反面
    /// </summary>
    private bool _level;

    /// <summary>
    /// 上一次写的时刻（Stopwatch 时间戳），写没写成都算；0 = 还没写过
    /// </summary>
    private long _lastWriteAt;

    private volatile bool _alive;

    public bool IsAlive => _alive;

    #endregion

    private IIo? _io;

    public override void Bind()
    {
        _io = IocHelper.GetService<IIo>();
    }

    #region 扫描

    protected override void OnScan()
    {
        base.OnScan();
        if (DoIndex < 0)
        {
            return;
        }

        if (_lastWriteAt != 0 && Stopwatch.GetElapsedTime(_lastWriteAt).TotalMilliseconds < IntervalMs)
        {
            return;
        }

        _lastWriteAt = Stopwatch.GetTimestamp();
        bool next = !_level;
        if (!WriteDo(next))
        {
            _alive = false;
            RaiseAlarm(SendFailedAlarm);
            return;
        }

        _level = next;
        _alive = true;
    }

    private bool WriteDo(bool on)
    {
        var io = _io;
        if (io is null)
        {
            return false;
        }

        return io.WriteDo(DoIndex, on).IsSuccess;
    }

    #endregion
}
