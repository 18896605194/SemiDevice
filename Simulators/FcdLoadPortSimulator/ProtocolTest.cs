using System.Text;

namespace FcdLoadPortSimulator;

/// <summary>
/// 协议逻辑自测 —— 直接测 B 类报文的指令分发与回复格式，不依赖串口。
/// 界面"协议自测"按钮触发, 结果写入收发日志区。
/// 协议: LP300 ASCII v2.4.4 (B类, 无校验)
/// </summary>
public class ProtocolTest
{
    private readonly Action<string> _write;
    private int _passCount;
    private int _failCount;

    public ProtocolTest(Action<string> write) => _write = write;

    public void Run()
    {
        _write("======== LP300 协议逻辑测试 (B类报文) ========");
        _write("帧格式: s00<TYPE>:<NAME>[/数据];<CR>");

        // ========== MOV 指令 (ACK → INF) ==========
        TestCase("T01", "s00MOV:CLOAD;", "Load操作", "s00ACK:CLOAD;", "s00INF:CLOAD/" + BuildLoadedMap() + ";");
        TestCase("T02", "s00MOV:CULOD;", "Unload操作", "s00ACK:CULOD;", "s00INF:CULOD;");
        TestCase("T03", "s00MOV:ORGSH;", "整机回零", "s00ACK:ORGSH;", "s00INF:ORGSH;");
        TestCase("T04", "s00MOV:CLDOP;", "门吸住/Unlatch解锁", "s00ACK:CLDOP;", "s00INF:CLDOP;");
        TestCase("T05", "s00MOV:PODCL;", "Lock FOUP", "s00ACK:PODCL;", "s00INF:PODCL;");
        TestCase("T06", "s00MOV:CUDCL;", "Undock", "s00ACK:CUDCL;", "s00INF:CUDCL;");
        TestCase("T07", "s00MOV:NOCMD;", "未知MOV指令 → NAK/0103", "s00NAK:NOCMD/0103;");

        // ========== GET 指令 (单帧 ACK + 数据) ==========
        TestCase("T08", "s00GET:STATE;", "获取状态", "s00ACK:STATE/" + BuildStateData() + ";");
        TestCase("T09", "s00GET:MAPDT;", "Mapping数据(正序)", "s00ACK:MAPDT/" + BuildMap() + ";");
        TestCase("T10", "s00GET:MAPRD;", "Mapping数据(逆序)", "s00ACK:MAPRD/" + BuildMap() + ";");
        TestCase("T11", "s00GET:VERSN;", "版本信息", "s00ACK:VERSN/" + Version + ";");
        TestCase("T12", "s00GET:OUPUT;", "输出状态", "s00ACK:OUPUT/" + BuildOutput() + ";");
        TestCase("T13", "s00GET:NOQRY;", "未知GET指令 → NAK/0103", "s00NAK:NOQRY/0103;");

        // ========== SET 指令 (ACK → INF, 回抄参数) ==========
        TestCase("T14", "s00SET:RESET;", "复位", "s00ACK:RESET;", "s00INF:RESET;");
        TestCase("T15", "s00SET:E84EN/01;", "E84激活(带参数)", "s00ACK:E84EN/01;", "s00INF:E84EN/01;");
        TestCase("T16", "s00SET:OUPUT/11/01;", "设置输出(带参数)", "s00ACK:OUPUT/11/01;", "s00INF:OUPUT/11/01;");
        TestCase("T17", "s00SET:NOSET;", "未知SET指令 → NAK/0103", "s00NAK:NOSET/0103;");

        // ========== 错误格式 ==========
        TestCase("T18", "s00ABC:XYZ;", "未知指令类型 → NAK/0103", "s00NAK:XYZ/0103;");
        TestCase("T19", "s00INVALIDDATA;", "格式错误(无冒号) → NAK/0107", "s00NAK:INVALIDDATA/0107;");

        _write($"======== 测试完成: {_passCount} 通过, {_failCount} 失败 ========");
    }

    /// <summary>
    /// 通用测试: 模拟一帧命令, 校验产生的响应帧序列与期望一致。
    /// </summary>
    private void TestCase(string id, string rawFrame, string description, params string[] expected)
    {
        List<string> actual = Simulate(rawFrame);

        bool pass = actual.Count == expected.Length;
        if (pass)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                if (!string.Equals(actual[i], expected[i], StringComparison.Ordinal))
                {
                    pass = false;
                    break;
                }
            }
        }

        if (pass)
        {
            _write($"[{id}] ✅ {description} → {string.Join(" | ", actual)}");
            _passCount++;
        }
        else
        {
            _write($"[{id}] ❌ {description}");
            _write($"       期望: {string.Join(" | ", expected)}");
            _write($"       实际: {string.Join(" | ", actual)}");
            _failCount++;
        }
    }

    /// <summary>
    /// 模拟命令分发, 返回响应帧序列(同步, 无延迟)。与 MainWindow.HandleFrame 的表驱动分发独立,
    /// 校验的是协议本身(默认表)的指令覆盖与封帧格式。
    /// </summary>
    private static List<string> Simulate(string rawFrame)
    {
        var responses = new List<string>();

        string command = StripFrame(rawFrame);

        int colonIndex = command.IndexOf(':');
        if (colonIndex != 3)
        {
            responses.Add(Build("NAK", command, "0107"));
            return responses;
        }

        string type = command.Substring(0, 3);
        string body = command.Substring(colonIndex + 1);
        string[] segs = body.Split('/');
        string name = segs[0];
        string[] args = segs.Length > 1 ? segs.Skip(1).ToArray() : Array.Empty<string>();

        switch (type)
        {
            case "MOV":
                if (!IsKnownMovement(name))
                {
                    responses.Add(Build("NAK", name, "0103"));
                    break;
                }
                responses.Add(Build("ACK", name));
                responses.Add(name is "CLOAD" or "CLDMP" or "CLMPO"
                    ? Build("INF", name, BuildLoadedMap())   // mapping 指令 INF 携带 map
                    : Build("INF", name));                    // 仿真 MOV 默认成功
                break;

            case "SET":
                if (!IsKnownSet(name))
                {
                    responses.Add(Build("NAK", name, "0103"));
                    break;
                }
                responses.Add(Build("ACK", name, args));
                responses.Add(Build("INF", name, args));
                break;

            case "GET":
                string? data = GetQueryData(name);
                responses.Add(data == null ? Build("NAK", name, "0103") : Build("ACK", name, data));
                break;

            default:
                responses.Add(Build("NAK", name, "0103"));
                break;
        }

        return responses;
    }

    private static string? GetQueryData(string name) => name switch
    {
        "STATE" => BuildStateData(),
        "MAPDT" => BuildMap(),
        "MAPRD" => BuildMap(),
        "VERSN" => Version,
        "OUPUT" => BuildOutput(),
        _ => null,
    };

    private static bool IsKnownMovement(string name) => name is
        "ORGSH" or "CLOAD" or "CULOD" or "CLDMP" or "CULDK" or "CULYD" or "CUDCL" or
        "CLDYD" or "CLDOP" or "CLMPO" or "PODCL" or "PODOP" or "RESUM" or "PAUSE" or "ABORT";

    private static bool IsKnownSet(string name) => name is "RESET" or "OUPUT" or "E84EN" or "E84ES";

    // ===== 封帧/拆帧/数据构造 =====

    private const string Version = "02-04-04-LP300SIM";

    /// <summary>构建 B 类响应帧: s00&lt;TYPE&gt;:&lt;NAME&gt;[/参数...];  (省略尾部 CR 便于比对)</summary>
    private static string Build(string type, string name, params string[] args)
    {
        var sb = new StringBuilder();
        sb.Append("s00").Append(type).Append(':').Append(name);
        foreach (var a in args)
        {
            if (!string.IsNullOrEmpty(a))
            {
                sb.Append('/').Append(a);
            }
        }
        sb.Append(';');
        return sb.ToString();
    }

    private static string StripFrame(string raw)
    {
        string s = raw.Trim();
        if (s.StartsWith("$1")) s = s[2..];
        if (s.Length >= 3 && s[0] == 's') s = s.Substring(3);
        if (s.EndsWith(";")) s = s[..^1];
        return s.Trim();
    }

    // 初始状态下的 STATE/MAP/OUPUT 数据 (与默认应答表一致)
    private static string BuildStateData()
    {
        char[] s = new char[64];
        for (int i = 0; i < s.Length; i++) s[i] = '0';
        s[0] = '1'; // Pod Presence
        s[1] = '1'; // PIP Placement
        s[3] = '1'; // Table Out (未对接)
        return new string(s);
    }

    private static string BuildMap() => new('E', 25);        // 默认全空片

    private static string BuildLoadedMap() => new('P', 25);  // 25 槽都有片

    private static string BuildOutput() => new('F', 64);     // 默认全无信号
}
