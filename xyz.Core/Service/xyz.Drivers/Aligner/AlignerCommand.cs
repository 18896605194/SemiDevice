namespace xyz.Drivers.Aligner;

/// <summary>
/// 对准器的一条指令：品牌驱动继承它（拼报文、解析回复），模块只看做没做完和结果
/// </summary>
public abstract class AlignerCommand
{
    private readonly object _completeGate = new();
    private volatile AlignerResponse? _response;
    private volatile bool _isCompleted;

    /// <summary>
    /// 做完了没有，成功失败都算
    /// </summary>
    public bool IsCompleted => _isCompleted;

    /// <summary>
    /// 结果，没做完为 null
    /// </summary>
    public AlignerResponse? Response => _response;

    /// <summary>
    /// 落终态：先写结果再置完成；已经完成的不改（回复和作废可能从两条线程同时来，先到的算）
    /// </summary>
    protected void Complete(AlignerResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_completeGate)
        {
            if (_isCompleted)
            {
                return;
            }

            _response = response;
            _isCompleted = true;
        }
    }
}
