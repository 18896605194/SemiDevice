namespace xyz.Modules;

/// <summary>
/// 一次读码的结果。
/// </summary>
/// <param name="IsSuccess">是否读到。</param>
/// <param name="CarrierId">载具 ID，失败时为空。</param>
/// <param name="Error">失败原因，成功时为空。</param>
public sealed record RfidReadResult(bool IsSuccess, string CarrierId, string Error)
{
    public static RfidReadResult Success(string carrierId)
    {
        return new RfidReadResult(true, carrierId, string.Empty);
    }

    public static RfidReadResult Failure(string error)
    {
        return new RfidReadResult(false, string.Empty, error);
    }
}

/// <summary>
/// RFID 读头能力契约：LoadPort 基类只依赖本接口，不同机型可换不同品牌的读头实现。
///
/// 读码是**非阻塞**的：BeginRead 只发起，结果由所属模块在扫描周期里 TakeResult 取。
/// 不做成同步返回是因为读头收发要走好几轮握手（几百毫秒），而模块扫描线程 50ms 一拍，
/// 阻塞在这儿会把整个 LoadPort 的轮询连同在途操作一起卡住。
/// </summary>
public interface IRfidReader
{
    /// <summary>
    /// 打开读头连接（串口/网口）；由所属模块 Open 时调用。
    /// </summary>
    bool Open();

    /// <summary>
    /// 关闭读头连接，释放串口/网口资源。
    /// </summary>
    void Close();

    /// <summary>
    /// 发起一次读码。未连接或上一次还没读完返回 false。
    /// </summary>
    bool BeginRead();

    /// <summary>
    /// 是否有读码在途。
    /// </summary>
    bool IsReading { get; }

    /// <summary>
    /// 取走最近一次读码结果；还没出结果返回 null。取走即清，同一个结果只会拿到一次。
    /// </summary>
    RfidReadResult? TakeResult();
}
