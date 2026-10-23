using System.Text;
using RfidSmoke;
using xyz._35021.Module.Drivers.Fcd.Rfid;
using xyz._35021.Module.Drivers.Fcd.Rfid.Commands;
using xyz.Components;
using xyz.Components.Components;
using xyz.Drivers.Communication;
using xyz.Shared.Dtos;

// FCD RFID 读头冒烟：不开硬件，用假读头按真实握手时序对话；读头类、解码器（拆帧 + 握手）、指令、通道都是生产代码，只把串口换成假读头。
// 覆盖：封壳拆壳、解码器拆帧解报文、握手（ENQ/EOT/ACK/NAK 双向）、没挂通道开机就抛、读码、载具 ID 切片、设备回错、拒收、坏块、
// 主动事件走上报、超时让出在途位、读头一次只认一条、版本和状态指令、关连接。
SmokeIoc.Reset();

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("FAIL: " + message);
    }

    checks++;
}

// ── 1. 协议层：封壳与拆壳 ────────────────────────────────────────────────
{
    var block = new byte[] { FcdRfidProtocol.CmdReadTag, 0x00 };
    var frame = FcdRfidProtocol.WrapBlock(block);
    Check(frame.Length == 1 + 2 + 2, "帧应是 LEN + 块 + 两字节校验");
    Check(frame[0] == 2, "LEN 应是块字节数（含 MSG_ID）");
    Check(frame[1] == 0xA0 && frame[2] == 0x00, "块内容应原样带上");

    // 校验和不含 LEN 字节：0xA0 + 0x00 = 0x00A0
    Check(frame[3] == 0x00 && frame[4] == 0xA0, $"校验和应只算块内字节，实际 {frame[3]:X2}{frame[4]:X2}");

    Check(FcdRfidProtocol.TryUnwrapBlock(frame, out byte id, out byte[] data), "自己封的帧应能拆回来");
    Check(id == 0xA0 && data.Length == 1 && data[0] == 0x00, "拆出的 MSG_ID 与数据应一致");

    var bad = FcdRfidProtocol.WrapBlock(block);
    bad[^1] ^= 0xFF;
    Check(!FcdRfidProtocol.TryUnwrapBlock(bad, out _, out _), "校验和不对应当拆解失败");
    Check(!FcdRfidProtocol.TryUnwrapBlock([0x02, 0xA0], out _, out _), "长度不够的残帧应当拆解失败");
}

// ── 2. 解码器：控制字符直通、块按长度收齐、解成报文 ──────────────────────
{
    var codec = new FcdRfidCodec();
    string enq = Text([FcdRfidProtocol.Enq]);
    Check(codec.Wrap(enq) == enq, "单字节控制字符应原样发，不封壳");

    string wrapped = codec.Wrap(Text([0xA0, 0x00]));
    Check(FcdRfidProtocol.Binary.GetBytes(wrapped).Length == 5, "非控制内容应按块封壳");

    // 读头 ENQ 之后必定跟一块：分两次喂进去也要能收齐
    var frames = codec.Extract(enq).ToList();
    Check(frames.Count == 1 && FcdRfidProtocol.Binary.GetBytes(frames[0])[0] == FcdRfidProtocol.Enq,
        "ENQ 应单字节成帧");

    var block = FcdRfidProtocol.WrapBlock([FcdRfidProtocol.RspStatus, 0x00]);
    frames = codec.Extract(Text(block[..2])).ToList();
    Check(frames.Count == 0, "块没收齐前不该成帧");
    frames = codec.Extract(Text(block[2..])).ToList();
    Check(frames.Count == 1 && FcdRfidProtocol.Binary.GetBytes(frames[0]).Length == block.Length,
        "块收齐后应整块成帧");

    // 收完块回到控制字节相位
    frames = codec.Extract(Text([FcdRfidProtocol.Ack])).ToList();
    Check(frames.Count == 1 && FcdRfidProtocol.Binary.GetBytes(frames[0])[0] == FcdRfidProtocol.Ack,
        "收完块应回到控制字节相位");

    // 解报文：指令拿到的是报文对象，不是字节
    var control = codec.Decode(enq) as FcdRfidMessage;
    Check(control is not null && control.IsControl && control.Control == FcdRfidProtocol.Enq, "控制字符解成控制帧");
    var status = codec.Decode(Text(block)) as FcdRfidMessage;
    Check(status is not null && !status.IsControl && status.IsValid && status.MessageId == FcdRfidProtocol.RspStatus
          && status.Data.SequenceEqual(new byte[] { 0x00 }),
        "校验过了的块解出 MSG_ID 和数据");
    var corrupt = FcdRfidProtocol.WrapBlock([FcdRfidProtocol.RspStatus, 0x00]);
    corrupt[^1] ^= 0xFF;
    var broken = codec.Decode(Text(corrupt)) as FcdRfidMessage;
    Check(broken is not null && !broken.IsControl && !broken.IsValid, "校验不过的块也交出去（标成坏块，链路要回 NAK）");
}

// ── 3. 握手（链路层，写在解码器里）：主机发指令 ENQ → EOT → 块 → ACK/NAK；读头回结果 ENQ → EOT → 块 → ACK/NAK ─────
{
    var link = new FcdRfidCodec();
    string body = Text([FcdRfidProtocol.CmdReadTag, 0x00]);
    var first = link.Start(body);
    Check(first.Count == 1 && first[0] == Text([FcdRfidProtocol.Enq]), "发指令先只发 ENQ，块留着");

    var eot = link.Receive(Control(FcdRfidProtocol.Eot));
    Check(eot.Consumed && eot.Replies.Count == 1 && eot.Replies[0] == body, "读头回 EOT 才把块发出去，EOT 不交给指令");
    Check(link.Receive(Control(FcdRfidProtocol.Eot)).Replies.Count == 0, "多来的 EOT 不重发块");

    var nak = link.Receive(Control(FcdRfidProtocol.Nak));
    Check(nak.Consumed && nak.Rejected == "NAK", "块发出去后读头回 NAK：拒收，在途指令立刻作废");
    Check(link.Receive(Control(FcdRfidProtocol.Nak)).Rejected is null, "没有在等回执时的 NAK 不算拒收");

    link.Start(body);
    link.Receive(Control(FcdRfidProtocol.Eot));
    var ack = link.Receive(Control(FcdRfidProtocol.Ack));
    Check(ack.Consumed && ack.Rejected is null && link.Receive(Control(FcdRfidProtocol.Nak)).Rejected is null,
        "读头 ACK 受理了：握手收相位，之后的 NAK 不再算这条的");

    var enq = link.Receive(Control(FcdRfidProtocol.Enq));
    Check(enq.Consumed && enq.Replies.Count == 1 && enq.Replies[0] == Text([FcdRfidProtocol.Eot]), "读头要发结果先 ENQ：主机回 EOT 放行");

    var good = link.Receive(new FcdRfidMessage { IsValid = true, MessageId = FcdRfidProtocol.RspTagData, Data = [0x00] });
    Check(!good.Consumed && good.Replies.Count == 1 && good.Replies[0] == Text([FcdRfidProtocol.Ack]),
        "好块：主机回 ACK，块接着交给指令");
    var bad = link.Receive(new FcdRfidMessage { IsValid = false });
    Check(bad.Consumed && bad.Replies.Count == 1 && bad.Replies[0] == Text([FcdRfidProtocol.Nak]),
        "坏块：主机回 NAK，不交给指令");

    link.Start(body);
    link.Reset();
    Check(link.Receive(Control(FcdRfidProtocol.Eot)).Replies.Count == 0, "连接重开（Reset）以后，发到一半的块作废");
}

// ── 4. 装配：没挂通道开机就抛 ───────────────────────────────────────────
{
    var bare = new SmokeRfidReader();
    Name(bare, "RFID", "SmokeLoadPort.RFID");
    bool threw = false;
    try
    {
        bare.InitComponent();
    }
    catch (InvalidOperationException)
    {
        threw = true;
    }

    Check(threw, "sc 里读头下面没挂通道：组件初始化就抛");
}

// ── 5. 读码正常路径 ─────────────────────────────────────────────────────
var (reader, channel, device) = Build();
Check(reader.InitComponent(), "读头组件初始化：认下面的通道、交解码器，通道在自己的初始化里连上");
Check(reader.IsConnected && channel.IsConnected, "通道处于已连接");

Check(reader.StartReadCarrierId(), "应能发起读码");
Check(reader.IsReading, "发起后应处于读码在途");
Check(!reader.StartReadCarrierId(), "同一时刻不该受理第二条读码");
Check(!channel.Send(new FcdGetStatusCommand()), "读头一次只认一条：读码在途时别的指令也发不出去");

var result = PumpUntilResult(reader);
Check(result is not null && result.IsSuccess, "读码应成功，实际: " + (result?.ErrorMessage ?? "无结果"));
Check(result!.Result == "FOUP-0001", $"应切出载具 ID，实际 \"{result.Result}\"");
Check(reader.GetCarrierIdResult() is null, "结果取走即清，不该拿到第二次");
Check(!reader.IsReading, "读完应让出在途位");

// 主机确实按协议发了 0xA0 读标签指令
Check(device.HostBlocks.TryDequeue(out var sent), "假读头应收到一条指令块");
Check(FcdRfidProtocol.TryUnwrapBlock(sent!, out byte sentId, out byte[] sentData)
      && sentId == FcdRfidProtocol.CmdReadTag && sentData.Length == 1 && sentData[0] == 0x00,
    "读码指令应是 0xA0 + 起始页 0");

// 在途位已让出：第二次读码照样能走
Check(reader.StartReadCarrierId(), "第一次读完后应能再读");
result = PumpUntilResult(reader);
Check(result is not null && result.IsSuccess && result.Result == "FOUP-0001", "第二次读码也应成功");

// ── 6. 载具 ID 切片：起始偏移与长度 ──────────────────────────────────────
{
    var (sliced, _, tag) = Build();
    sliced.IdStart = 5;
    sliced.IdLength = 4;
    tag.TagMemory = Encoding.ASCII.GetBytes("FOUP-0001       ");
    sliced.InitComponent();
    sliced.StartReadCarrierId();
    var slicedResult = PumpUntilResult(sliced);
    Check(slicedResult is not null && slicedResult.IsSuccess && slicedResult.Result == "0001",
        $"应按 IdStart/IdLength 切片，实际 \"{slicedResult?.Result}\"");
}

// 尾部填充要去掉
{
    var (padded, _, tag) = Build();
    tag.TagMemory = Encoding.ASCII.GetBytes("ABC\0\0\0\0\0\0\0\0\0\0\0\0\0");
    padded.InitComponent();
    padded.StartReadCarrierId();
    var paddedResult = PumpUntilResult(padded);
    Check(paddedResult is not null && paddedResult.IsSuccess && paddedResult.Result == "ABC",
        $"标签尾部的空格与 NUL 应去掉，实际 \"{paddedResult?.Result}\"");
}

// ── 7. 设备回错误块 0x63 ────────────────────────────────────────────────
{
    var (failing, _, tag) = Build();
    tag.ErrorCode = 0x02;
    failing.InitComponent();
    failing.StartReadCarrierId();
    var errorResult = PumpUntilResult(failing);
    Check(errorResult is not null && !errorResult.IsSuccess, "错误块应落读码失败");
    Check(errorResult!.ErrorMessage == "ERR02", $"应带出设备错误码，实际 \"{errorResult.ErrorMessage}\"");
    Check(!failing.IsReading, "失败后也要让出在途位");
    Check(failing.StartReadCarrierId(), "失败之后应还能再读");
}

// ── 8. 读头拒收（NAK）──────────────────────────────────────────────────
{
    var (rejected, _, tag) = Build();
    tag.RejectCommand = true;
    rejected.InitComponent();
    rejected.StartReadCarrierId();
    var nakResult = PumpUntilResult(rejected);
    Check(nakResult is not null && !nakResult.IsSuccess && nakResult.ErrorMessage == "NAK",
        $"读头拒收应立刻落失败而不是干等超时，实际 \"{nakResult?.ErrorMessage}\"");
}

// ── 9. 结果块校验和坏掉：主机应回 NAK，不能把坏数据当 ID ──────────────────
{
    var (corrupt, _, tag) = Build();
    tag.CorruptChecksum = true;
    corrupt.InitComponent();
    corrupt.StartReadCarrierId();
    Pump(corrupt, 500);
    Check(Volatile.Read(ref tag.HostNakCount) > 0, "校验不过的块主机应回 NAK");
    Check(corrupt.GetCarrierIdResult() is null, "坏块不该产生读码结果");
    Check(corrupt.IsReading, "坏块不终结指令，应继续等到超时");
}

// ── 10. 主动事件 0x66 不冒充在途指令的结果，走通道的上报 ───────────────────
{
    var (listening, listeningChannel, tag) = Build();
    var events = new List<FcdRfidMessage>();
    listening.InitComponent();
    listeningChannel.Reported += message =>
    {
        if (message is FcdRfidMessage rfid)
        {
            lock (events)
            {
                events.Add(rfid);
            }
        }
    };

    tag.PushSpontaneousEvent(0x07);
    Pump(listening, 300);
    lock (events)
    {
        Check(events.Count == 1 && events[0].MessageId == FcdRfidProtocol.RspEvent && events[0].Data.SequenceEqual(new byte[] { 0x07 }),
            $"0x66 应走通道的上报，实际 {events.Count} 条");
    }

    Check(listening.GetCarrierIdResult() is null, "主动事件不该产生读码结果");
}

// ── 11. 超时：读头不回结果 ──────────────────────────────────────────────
{
    var (silent, _, tag) = Build();
    tag.SwallowResult = true;
    silent.InitComponent();
    Check(silent.StartReadCarrierId(), "应能发起读码");

    int timeout = silent.ReadCarrierIdTimeout;
    Check(timeout == 5000, $"读码超时默认应为 5000ms，实际 {timeout}");

    var timedOut = PumpUntilResult(silent, timeout + 1500);
    Check(timedOut is not null && !timedOut.IsSuccess, "读头不回结果应落超时失败");
    Check(timedOut!.ErrorMessage.Contains("超时"), $"失败原因应说明是超时，实际 \"{timedOut.ErrorMessage}\"");
    Check(!silent.IsReading, "超时后必须让出在途位");
    Check(silent.StartReadCarrierId(), "超时之后应还能再发起读码——在途位没让出来的话这里会挂");
}

// ── 12. 其它指令：版本与状态（直接经通道发）────────────────────────────────
{
    var (other, otherChannel, _) = Build();
    other.InitComponent();

    var version = new FcdGetVersionCommand();
    Check(otherChannel.Send(version), "应能下发取版本指令");
    Check(version.WaitReply(2000), "取版本应有回复");
    Check(version.IsSuccess && version.Data.Length == 2, "取版本应带回版本数据");

    var status = new FcdGetStatusCommand();
    Check(otherChannel.Send(status), "上一条做完后应能下发取状态指令");
    Check(status.WaitReply(2000), "取状态应有回复");
    Check(status.IsSuccess, "取状态应成功");

    other.Close();
    Check(!other.IsConnected && !other.StartReadCarrierId(), "关连接以后断开，读码发不起来");
}

// ── 13. 每条指令自己认回复（Handle 写在指令里，品牌基类不管）────────────────
{
    var read = new FcdReadCarrierIdCommand(0, 16);
    Check(!read.Accept(Control(FcdRfidProtocol.Ack)) && !read.Accept(new FcdRfidMessage { IsValid = false }) && !read.IsCompleted,
        "握手控制字符、校验不过的块不认（链路层已经处理过）");
    Check(!read.Accept(new FcdRfidMessage { IsValid = true, MessageId = FcdRfidProtocol.RspStatus, Data = [0x00] }) && !read.IsCompleted,
        "别的指令的响应块（0x61）不认");
    Check(read.Accept(new FcdRfidMessage { IsValid = true, MessageId = FcdRfidProtocol.RspTagData, Data = [0x00, 0x01] })
          && !read.IsSuccess && read.Error == "EmptyTag",
        "标签数据块里没有内容：读码判失败（EmptyTag）");
    var status = new FcdGetStatusCommand();
    Check(status.Accept(new FcdRfidMessage { IsValid = true, MessageId = FcdRfidProtocol.RspError, Data = [] })
          && !status.IsSuccess && status.Error == "ERR",
        "错误块能终结任何指令，没带错误号原因就是 ERR");
}

Console.WriteLine($"PASS: {checks} rfid checks (FCD RFT-200S 协议、解码器、双向握手、切片、在途位与超时、主动上报、每条指令自己认回复；不连硬件)。");

// ── 辅助 ───────────────────────────────────────────────────────────────

static (SmokeRfidReader Reader, SmokeChannel Channel, FakeFcdReader Device) Build()
{
    var device = new FakeFcdReader();
    var reader = new SmokeRfidReader();
    var channel = new SmokeChannel(device);
    Name(reader, "RFID", "SmokeLoadPort.RFID");
    Name(channel, "Driver", "SmokeLoadPort.RFID.Driver");
    reader.AddChild(channel);
    return (reader, channel, device);
}

static void Name(ComponentBase component, string name, string path)
{
    typeof(ComponentBase).GetProperty(nameof(ComponentBase.Name))!.SetValue(component, name);
    typeof(ComponentBase).GetProperty(nameof(ComponentBase.FullPath))!.SetValue(component, path);
}

static string Text(byte[] bytes)
{
    return FcdRfidProtocol.Binary.GetString(bytes);
}

static FcdRfidMessage Control(byte control)
{
    return new FcdRfidMessage { IsControl = true, Control = control };
}

static void Pump(SmokeRfidReader reader, int milliseconds)
{
    var deadline = Environment.TickCount + milliseconds;
    while (Environment.TickCount < deadline)
    {
        reader.Tick();
        Thread.Sleep(10);
    }
}

static HandleResult<string>? PumpUntilResult(SmokeRfidReader reader, int milliseconds = 2000)
{
    var deadline = Environment.TickCount + milliseconds;
    while (Environment.TickCount < deadline)
    {
        reader.Tick();
        var result = reader.GetCarrierIdResult();
        if (result is not null)
        {
            return result;
        }

        Thread.Sleep(10);
    }

    return null;
}

namespace RfidSmoke
{
    /// <summary>
    /// 机型读头类的冒烟版：只多一个推扫描的口子，读码流程、解码器、指令都是生产代码
    /// </summary>
    internal sealed class SmokeRfidReader : FcdRfidReader
    {
        /// <summary>
        /// 顶替所属 LoadPort 的扫描线程推一拍
        /// </summary>
        public void Tick()
        {
            OnScan();
        }
    }

    /// <summary>
    /// 平台通道的冒烟版：只把串口换成假读头，收发、在途表、握手都是生产代码
    /// </summary>
    internal sealed class SmokeChannel(FakeFcdReader device) : DeviceDriverComponent
    {
        protected override ICommunication CreateTransport()
        {
            return device;
        }
    }
}
