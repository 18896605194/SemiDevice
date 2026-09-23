using System.Text;
using RfidSmoke;
using xyz.Components;
using xyz.Drivers.Communication;
using xyz.Drivers.Rfid;
using xyz.Drivers.Rfid.FCD;
using xyz.Drivers.Rfid.FCD.Commands;
using xyz.Components.Components;
using xyz.Modules;

// FCD RFID 读头冒烟：不开硬件，用假读头按真实握手时序对话。
// 覆盖帧编解码、ENQ/EOT/ACK/NAK 双向握手、块校验、载具 ID 切片、在途位管理与超时。
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
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

// ── 2. 帧编解码：控制字符直通、块按长度收齐 ──────────────────────────────
{
    var codec = new FcdRfidFrameCodec();
    string enq = FcdRfidProtocol.Binary.GetString([FcdRfidProtocol.Enq]);
    Check(codec.Wrap(enq) == enq, "单字节控制字符应原样发，不封壳");

    string wrapped = codec.Wrap(FcdRfidProtocol.Binary.GetString([0xA0, 0x00]));
    Check(FcdRfidProtocol.Binary.GetBytes(wrapped).Length == 5, "非控制内容应按块封壳");

    // 读头 ENQ 之后必定跟一块：分两次喂进去也要能收齐
    var frames = codec.Extract(FcdRfidProtocol.Binary.GetString([FcdRfidProtocol.Enq])).ToList();
    Check(frames.Count == 1 && FcdRfidProtocol.Binary.GetBytes(frames[0])[0] == FcdRfidProtocol.Enq,
        "ENQ 应单字节成帧");

    var block = FcdRfidProtocol.WrapBlock([FcdRfidProtocol.RspStatus, 0x00]);
    frames = codec.Extract(FcdRfidProtocol.Binary.GetString(block[..2])).ToList();
    Check(frames.Count == 0, "块没收齐前不该成帧");
    frames = codec.Extract(FcdRfidProtocol.Binary.GetString(block[2..])).ToList();
    Check(frames.Count == 1 && FcdRfidProtocol.Binary.GetBytes(frames[0]).Length == block.Length,
        "块收齐后应整块成帧");

    // 收完块回到控制字节相位
    frames = codec.Extract(FcdRfidProtocol.Binary.GetString([FcdRfidProtocol.Ack])).ToList();
    Check(frames.Count == 1 && FcdRfidProtocol.Binary.GetBytes(frames[0])[0] == FcdRfidProtocol.Ack,
        "收完块应回到控制字节相位");
}

// ── 3. 读码正常路径 ─────────────────────────────────────────────────────
var (reader, device) = Build();
Check(reader.Open(), "假读头应能打开");
Check(reader.Driver is not null && reader.Driver.IsConnected, "驱动应处于已连接");

Check(reader.BeginRead(), "应能发起读码");
Check(reader.IsReading, "发起后应处于读码在途");
Check(!reader.BeginRead(), "同一时刻不该受理第二条读码");

var result = PumpUntilResult(reader);
Check(result is { IsSuccess: true }, "读码应成功，实际: " + (result?.Error ?? "无结果"));
Check(result!.CarrierId == "FOUP-0001", $"应切出载具 ID，实际 \"{result.CarrierId}\"");
Check(reader.TakeResult() is null, "结果取走即清，不该拿到第二次");
Check(!reader.IsReading, "读完应让出在途位");

// 主机确实按协议发了 0xA0 读标签指令
Check(device.HostBlocks.TryDequeue(out var sent), "假读头应收到一条指令块");
Check(FcdRfidProtocol.TryUnwrapBlock(sent!, out byte sentId, out byte[] sentData)
      && sentId == FcdRfidProtocol.CmdReadTag && sentData.Length == 1 && sentData[0] == 0x00,
    "读码指令应是 0xA0 + 起始页 0");

// 在途位已让出：第二次读码照样能走
Check(reader.BeginRead(), "第一次读完后应能再读");
result = PumpUntilResult(reader);
Check(result is { IsSuccess: true } && result.CarrierId == "FOUP-0001", "第二次读码也应成功");

// ── 4. 载具 ID 切片：起始偏移与长度 ──────────────────────────────────────
{
    var (sliced, tag) = Build();
    sliced.IdStart = 5;
    sliced.IdLength = 4;
    tag.TagMemory = Encoding.ASCII.GetBytes("FOUP-0001       ");
    sliced.Open();
    sliced.BeginRead();
    var slicedResult = PumpUntilResult(sliced);
    Check(slicedResult is { IsSuccess: true } && slicedResult.CarrierId == "0001",
        $"应按 IdStart/IdLength 切片，实际 \"{slicedResult?.CarrierId}\"");
}

// 尾部填充要去掉
{
    var (padded, tag) = Build();
    tag.TagMemory = Encoding.ASCII.GetBytes("ABC\0\0\0\0\0\0\0\0\0\0\0\0\0");
    padded.Open();
    padded.BeginRead();
    var paddedResult = PumpUntilResult(padded);
    Check(paddedResult is { IsSuccess: true } && paddedResult.CarrierId == "ABC",
        $"标签尾部的空格与 NUL 应去掉，实际 \"{paddedResult?.CarrierId}\"");
}

// ── 5. 设备回错误块 0x63 ────────────────────────────────────────────────
{
    var (failing, tag) = Build();
    tag.ErrorCode = 0x02;
    failing.Open();
    failing.BeginRead();
    var errorResult = PumpUntilResult(failing);
    Check(errorResult is { IsSuccess: false }, "错误块应落读码失败");
    Check(errorResult!.Error == "ERR02", $"应带出设备错误码，实际 \"{errorResult.Error}\"");
    Check(!failing.IsReading, "失败后也要让出在途位");
    Check(failing.BeginRead(), "失败之后应还能再读");
}

// ── 6. 读头拒收（NAK）──────────────────────────────────────────────────
{
    var (rejected, tag) = Build();
    tag.RejectCommand = true;
    rejected.Open();
    rejected.BeginRead();
    var nakResult = PumpUntilResult(rejected);
    Check(nakResult is { IsSuccess: false, Error: "NAK" },
        $"读头拒收应立刻落失败而不是干等超时，实际 \"{nakResult?.Error}\"");
}

// ── 7. 结果块校验和坏掉：主机应回 NAK，不能把坏数据当 ID ──────────────────
{
    var (corrupt, tag) = Build();
    tag.CorruptChecksum = true;
    corrupt.Open();
    corrupt.BeginRead();
    Pump(corrupt, 500);
    Check(Volatile.Read(ref tag.HostNakCount) > 0, "校验不过的块主机应回 NAK");
    Check(corrupt.TakeResult() is null, "坏块不该产生读码结果");
    Check(corrupt.IsReading, "坏块不终结指令，应继续等到超时");
}

// ── 8. 主动事件 0x66 不冒充在途指令的结果 ────────────────────────────────
{
    var (listening, tag) = Build();
    var events = new List<RfidResponse>();
    listening.Open();
    listening.Driver!.OnSpontaneousEvent += response => { lock (events) { events.Add(response); } };

    tag.PushSpontaneousEvent(0x07);
    Pump(listening, 300);
    lock (events)
    {
        Check(events.Count == 1 && events[0].Data.Length == 1 && events[0].Data[0] == 0x07,
            $"0x66 应走主动事件上抛，实际 {events.Count} 条");
    }

    Check(listening.TakeResult() is null, "主动事件不该产生读码结果");
}

// ── 9. 超时：读头不回结果 ───────────────────────────────────────────────
{
    var (silent, tag) = Build();
    tag.SwallowResult = true;
    silent.Open();
    Check(silent.BeginRead(), "应能发起读码");

    int timeout = silent.ReadCarrierIdTimeout;
    Check(timeout == 5000, $"读码超时默认应为 5000ms，实际 {timeout}");

    var timedOut = PumpUntilResult(silent, timeout + 1500);
    Check(timedOut is { IsSuccess: false }, "读头不回结果应落超时失败");
    Check(timedOut!.Error.Contains("超时"), $"失败原因应说明是超时，实际 \"{timedOut.Error}\"");
    Check(!silent.IsReading, "超时后必须让出在途位");
    Check(silent.BeginRead(), "超时之后应还能再发起读码——在途位没让出来的话这里会挂");
}

// ── 10. 其它指令：版本与状态 ────────────────────────────────────────────
{
    var (other, _) = Build();
    other.Open();
    var driver = other.Driver!;

    var version = new FcdGetVersionCommand();
    Check(driver.Submit(version), "应能下发取版本指令");
    Check(WaitCompleted(version), "取版本应有回复");
    Check(version.Response is { IsSuccess: true } && version.Response.Data.Length == 2,
        "取版本应带回版本数据");

    var status = new FcdGetStatusCommand();
    Check(driver.Submit(status), "上一条终结后应能下发取状态指令");
    Check(WaitCompleted(status), "取状态应有回复");
    Check(status.Response is { IsSuccess: true }, "取状态应成功");
}

Console.WriteLine($"PASS: {checks} RFID checks (FCD RFT-200S 协议、双向握手、切片、在途位与超时；不连硬件)。");

// ── 辅助 ───────────────────────────────────────────────────────────────

static (SmokeRfidReader Reader, FakeFcdReader Device) Build()
{
    var device = new FakeFcdReader();
    var reader = new SmokeRfidReader(device);
    typeof(ComponentBase).GetProperty("Name")!.SetValue(reader, "RFID");
    typeof(ComponentBase).GetProperty("FullPath")!.SetValue(reader, "SmokeLoadPort.RFID");
    return (reader, device);
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

static RfidReadResult? PumpUntilResult(SmokeRfidReader reader, int milliseconds = 2000)
{
    var deadline = Environment.TickCount + milliseconds;
    while (Environment.TickCount < deadline)
    {
        reader.Tick();
        if (reader.TakeResult() is { } result)
        {
            return result;
        }

        Thread.Sleep(10);
    }

    return null;
}

static bool WaitCompleted(RfidCommand command, int milliseconds = 2000)
{
    var deadline = Environment.TickCount + milliseconds;
    while (Environment.TickCount < deadline)
    {
        if (command.IsCompleted)
        {
            return true;
        }

        Thread.Sleep(10);
    }

    return false;
}

namespace RfidSmoke
{
    /// <summary>
    /// 机型组件的冒烟版：只把传输换成假读头，编解码/驱动/指令/步进机都是生产代码。
    /// </summary>
    internal sealed class SmokeRfidReader : FcdRfidComponent
    {
        private readonly FakeFcdReader _device;

        public SmokeRfidReader(FakeFcdReader device)
        {
            _device = device;
        }

        protected override IRfidDriver CreateDriver()
        {
            return new FcdRfidDriver(
                new FrameCommunication(_device, new FcdRfidFrameCodec(), FcdRfidProtocol.Binary));
        }

        /// <summary>顶替父模块的扫描线程推一拍。</summary>
        public void Tick()
        {
            OnScan();
        }
    }
}
