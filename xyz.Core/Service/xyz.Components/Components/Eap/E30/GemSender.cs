using System.Threading.Channels;
using xyz.Common.Log;
using xyz.Secs;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// GEM 往 Host 发报文的出口（事件报告 S6F11、报警 S5F1……）：一条专用线程按生成的先后一条一条发，等到回复再发下一条。
/// 每条该发、该缓存还是该丢，在这里按通讯和缓存的状态定：
/// 缓存开着 → Host 要缓存的进缓存、别的丢掉（缓存开着时新报文也进缓存，先后才不乱）；
/// 通讯没建立 → 丢掉（缓存只在断过通讯以后才开）；
/// 通讯好着 → 发，等回复；发的时候断了或者 T3 等不到回复算通讯断了（E30：设备发的报文等不到回复就是通讯故障），这一条再按缓存走。
/// Host 用 S6F23 要缓存 / 清缓存也排在这条线上做，跟新报文不抢先后。
/// </summary>
internal sealed class GemSender
{
    private readonly Channel<Work> _queue = Channel.CreateUnbounded<Work>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Func<HsmsComponent?> _link;
    private readonly GemSpool _spool;
    private readonly GemBook _book;
    private readonly Func<bool> _isCommunicating;
    private readonly Action<string> _communicationFailed;
    private readonly Action _spoolEmptied;
    private readonly Action _transmitFailed;
    private readonly Func<string> _owner;
    private int _started;
    private int _transmitting;

    /// <param name="link">链路（没配为 null）。</param>
    /// <param name="spool">缓存。</param>
    /// <param name="book">设定本（哪些报文要缓存）。</param>
    /// <param name="isCommunicating">通讯建立了没有。</param>
    /// <param name="communicationFailed">发的时候断了 / T3 超时：告诉 E30 通讯断了（它会开缓存）。</param>
    /// <param name="spoolEmptied">缓存取空或清掉了：E30 关缓存、报事件。</param>
    /// <param name="transmitFailed">发缓存的时候断了：E30 报事件。</param>
    /// <param name="owner">日志里写谁。</param>
    public GemSender(Func<HsmsComponent?> link, GemSpool spool, GemBook book, Func<bool> isCommunicating,
        Action<string> communicationFailed, Action spoolEmptied, Action transmitFailed, Func<string> owner)
    {
        _link = link;
        _spool = spool;
        _book = book;
        _isCommunicating = isCommunicating;
        _communicationFailed = communicationFailed;
        _spoolEmptied = spoolEmptied;
        _transmitFailed = transmitFailed;
        _owner = owner;
    }

    /// <summary>正在往 Host 发缓存（S6F23 要的）：这时候再要回 RSDA=1（忙）。</summary>
    public bool IsTransmitting => Volatile.Read(ref _transmitting) == 1;

    /// <summary>排一条要发的报文（任意线程）。</summary>
    public void Enqueue(SecsMessage message)
    {
        Post(new SendWork(message));
    }

    /// <summary>Host 要缓存（S6F23 RSDC=0）：最多发 max 条（0 = 全发）。</summary>
    public void Transmit(int max)
    {
        Interlocked.Exchange(ref _transmitting, 1);
        Post(new TransmitWork(max));
    }

    /// <summary>Host 让清缓存（S6F23 RSDC=1）。</summary>
    public void Purge()
    {
        Post(new PurgeWork());
    }

    private void Post(Work work)
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
        {
            _ = Task.Run(LoopAsync);
        }

        _queue.Writer.TryWrite(work);
    }

    private async Task LoopAsync()
    {
        await foreach (var work in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                switch (work)
                {
                    case SendWork send:
                        await SendOrSpoolAsync(send.Message).ConfigureAwait(false);
                        break;

                    case TransmitWork transmit:
                        await TransmitAsync(transmit.Max).ConfigureAwait(false);
                        break;

                    case PurgeWork:
                        _spool.Clear();
                        LogHelper.Info(_owner(), "Host 让清缓存，已清空");
                        _spoolEmptied();
                        break;
                }
            }
            catch (Exception exception)
            {
                LogHelper.Error(_owner(), $"GEM 发送出错：{exception.Message}");
            }
        }
    }

    private async Task SendOrSpoolAsync(SecsMessage message)
    {
        if (_spool.IsActive)
        {
            Spool(message);
            return;
        }

        if (!_isCommunicating())
        {
            LogHelper.Warn(_owner(), $"跟 Host 的通讯没建立，{message.Name} 没发（没开缓存）");
            return;
        }

        var link = _link();
        try
        {
            if (link is null)
            {
                throw new HsmsConnectionException("没配 EAP 链路");
            }

            var reply = await link.SendAsync(message).ConfigureAwait(false);
            CheckAck(message, reply.Body);
        }
        catch (Exception exception) when (exception is HsmsConnectionException or SecsTimeoutException)
        {
            _communicationFailed($"{message.Name} 没发成：{exception.Message}");
            if (_spool.IsActive)
            {
                Spool(message);
            }
            else
            {
                LogHelper.Warn(_owner(), $"{message.Name} 没发成也没缓存：{exception.Message}");
            }
        }
        catch (SecsException exception)
        {
            // Host 回了 S9 / SxF0：它不收这条，重发也没用
            LogHelper.Warn(_owner(), $"Host 没收 {message.Name}：{exception.Message}");
        }
    }

    /// <summary>缓存开着：Host 要缓存的进缓存，别的丢掉。</summary>
    private void Spool(SecsMessage message)
    {
        if (!_book.IsSpoolable(message.Stream, message.Function))
        {
            LogHelper.Warn(_owner(), $"缓存中，{message.Name} 不在 Host 要缓存的范围里，丢掉");
            return;
        }

        if (!_spool.Add(message))
        {
            LogHelper.Warn(_owner(), $"缓存满了，{message.Name} 丢掉");
        }
    }

    /// <summary>
    /// 按先后发缓存：发成一条删一条；发的时候断了停下来（这一条留着），报发送失败；Host 不收的那条删掉接着发（不然永远卡在它上面）。
    /// 发空了关缓存。
    /// </summary>
    private async Task TransmitAsync(int max)
    {
        try
        {
            int sent = 0;
            while (max <= 0 || sent < max)
            {
                var next = _spool.Oldest();
                if (next is null)
                {
                    break;
                }

                var (id, message) = next.Value;
                var link = _link();
                try
                {
                    if (link is null)
                    {
                        throw new HsmsConnectionException("没配 EAP 链路");
                    }

                    var reply = await link.SendAsync(message).ConfigureAwait(false);
                    CheckAck(message, reply.Body);
                }
                catch (Exception exception) when (exception is HsmsConnectionException or SecsTimeoutException)
                {
                    LogHelper.Warn(_owner(), $"发缓存的时候断了，还剩 {_spool.CountActual} 条：{exception.Message}");
                    _transmitFailed();
                    _communicationFailed($"发缓存没发成：{exception.Message}");
                    return;
                }
                catch (SecsException exception)
                {
                    LogHelper.Warn(_owner(), $"Host 没收缓存里的 {message.Name}，跳过：{exception.Message}");
                }

                _spool.Remove(id);
                sent++;
            }

            if (_spool.CountActual == 0)
            {
                LogHelper.Info(_owner(), $"缓存发完了（这一次发了 {sent} 条）");
                _spoolEmptied();
            }
            else
            {
                LogHelper.Info(_owner(), $"这一次发了 {sent} 条缓存，还剩 {_spool.CountActual} 条");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _transmitting, 0);
        }
    }

    /// <summary>S6F12 的 ACKC6、S5F2 的 ACKC5：0 是收下了，别的记一笔（Host 不收也不重发）。</summary>
    private void CheckAck(SecsMessage message, SecsItem? body)
    {
        if (body is null || body.Format != SecsFormat.Binary)
        {
            return;
        }

        var ack = body.GetBinary();
        if (ack.Length == 1 && ack[0] != 0)
        {
            LogHelper.Warn(_owner(), $"Host 回 {message.Name} 的应答码是 {ack[0]}（不是 0）");
        }
    }

    /// <summary>发送线程上排队的一件事。</summary>
    private abstract class Work
    {
    }

    private sealed class SendWork : Work
    {
        public SendWork(SecsMessage message)
        {
            Message = message;
        }

        public SecsMessage Message { get; }
    }

    private sealed class TransmitWork : Work
    {
        public TransmitWork(int max)
        {
            Max = max;
        }

        public int Max { get; }
    }

    private sealed class PurgeWork : Work
    {
    }
}
