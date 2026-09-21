using System.Runtime.InteropServices;
using xyz.Common.Log;

namespace xyz.Components.Components;

public partial class PlcComponent
{
    #region 输入订阅

    public IDisposable SubscribeInput<T>(string path, Action<T> received) where T : unmanaged
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(received);

        IDisposable notification = SubscribeDevice(path, received);
        try
        {
            received(ReadInitial<T>(path));
            return notification;
        }
        catch
        {
            notification.Dispose();
            throw;
        }
    }

    #endregion

    #region 输出订阅

    public IDisposable SubscribeOutput<T>(
        string path,
        Func<T?> desired,
        Action<T> initialize,
        Action<T> written) where T : unmanaged
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(initialize);
        ArgumentNullException.ThrowIfNull(written);

        T lastWritten = ReadInitial<T>(path);
        initialize(lastWritten);

        object writeGate = new();
        string? lastError = null;
        long generation = ConnectionGeneration;

        // 与 g 一样使用周期通知检查待发指令，无需订阅基类或独立扫描任务。
        return SubscribeDevice<T>(path, delegate(T feedback)
        {
            lock (writeGate)
            {
                if (!IsConnected || generation != ConnectionGeneration)
                {
                    return;
                }

                try
                {
                    T? pending = desired();
                    if (!pending.HasValue || EqualityComparer<T>.Default.Equals(pending.Value, lastWritten))
                    {
                        return;
                    }

                    T value = pending.Value;
                    byte[] data = new byte[Marshal.SizeOf<T>()];
                    MemoryMarshal.Write(data, in value);
                    if (!WriteDevice(path, data))
                    {
                        throw new InvalidOperationException($"写入 {path} 失败");
                    }

                    if (!IsConnected || generation != ConnectionGeneration)
                    {
                        return;
                    }

                    lastWritten = value;
                    written(value);
                    lastError = null;
                }
                catch (Exception exception)
                {
                    if (lastError != exception.Message)
                    {
                        LogHelper.Warn("Plc", $"轴指令发送失败: {exception.Message}");
                        lastError = exception.Message;
                    }
                }
            }
        }, cyclic: true);
    }

    #endregion

    #region 驱动接口

    protected virtual IDisposable SubscribeDevice<T>(
        string path,
        Action<T> received,
        bool cyclic = false) where T : unmanaged
    {
        throw new NotSupportedException($"{GetType().Name} 不支持 PLC 通知订阅");
    }

    private T ReadInitial<T>(string path) where T : unmanaged
    {
        if (!IsConnected || !ReadDevice(path, out var data) || data.Length != Marshal.SizeOf<T>())
        {
            throw new InvalidOperationException($"PLC 结构体读取失败或长度不匹配: {path}");
        }

        return MemoryMarshal.Read<T>(data);
    }

    #endregion
}
