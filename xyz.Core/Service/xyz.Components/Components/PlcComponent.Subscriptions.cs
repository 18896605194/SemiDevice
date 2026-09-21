using System.Runtime.InteropServices;
using xyz.Common.Log;

namespace xyz.Components.Components;

public partial class PlcComponent
{
    private readonly object _subscriptionGate = new();
    private readonly List<Subscription> _subscriptions = [];

    public IDisposable SubscribeInput<T>(string path, Action<T> received) where T : unmanaged
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(received);
        lock (_subscriptionGate)
        {
            var subscription = new InputSubscription<T>(this, path, received);
            _subscriptions.Add(subscription);
            return subscription;
        }
    }

    public IDisposable SubscribeOutput<T>(string path, Func<T> desired, Action<T> initialize,
        Action<T> written) where T : unmanaged
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(initialize);
        ArgumentNullException.ThrowIfNull(written);
        lock (_subscriptionGate)
        {
            if (_subscriptions.Any(s => s.IsOutput && s.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"PLC 输出路径已有订阅: {path}");
            var subscription = new OutputSubscription<T>(this, path, desired, initialize, written);
            _subscriptions.Add(subscription);
            return subscription;
        }
    }

    /// <summary>品牌驱动建立状态通知；不支持的驱动不能用空订阅伪装成功。</summary>
    protected virtual IDisposable SubscribeDevice<T>(string path, Action<T> received) where T : unmanaged
        => throw new NotSupportedException($"{GetType().Name} 不支持 PLC 通知订阅");

    private T ReadInitial<T>(string path) where T : unmanaged
    {
        if (!ReadDevice(path, out var data) || data.Length != Marshal.SizeOf<T>())
            throw new InvalidOperationException($"PLC 结构体读取失败或长度不匹配: {path}");
        return MemoryMarshal.Read<T>(data);
    }

    private void PumpSubscriptions()
    {
        lock (_subscriptionGate)
        {
            if (_closed || !IsConnected) return;
            foreach (var subscription in _subscriptions.ToArray())
            {
                if (!IsConnected || _closed) break;
                subscription.Pump();
            }
        }
    }

    private void ResetSubscriptions()
    {
        lock (_subscriptionGate)
            foreach (var subscription in _subscriptions.ToArray()) subscription.Reset();
    }

    private abstract class Subscription(PlcComponent owner, string path) : IDisposable
    {
        protected readonly PlcComponent Owner = owner;
        public string Path { get; } = path;
        public virtual bool IsOutput => false;
        protected long Generation = -1;
        protected bool Disposed;
        private string? _lastError;
        private long _retryAt;

        public void Pump()
        {
            if (Disposed) return;
            if (Generation != Owner.ConnectionGeneration) Reset();
            if (Environment.TickCount64 < _retryAt) return;
            try
            {
                if (Generation < 0)
                {
                    long generation = Owner.ConnectionGeneration;
                    Generation = generation;
                    Activate();
                    if (!Owner.IsConnected || generation != Owner.ConnectionGeneration)
                    {
                        Reset();
                        return;
                    }
                }
                Tick();
                _lastError = null;
            }
            catch (Exception ex)
            {
                // 已建立的输出保留 lastWritten，失败不能假装已发送，也不能重新取基线吞掉待发指令。
                if (!Owner.IsConnected || !IsOutput) Reset();
                if (_lastError != ex.Message)
                {
                    LogHelper.Warn("Plc", $"订阅 {Path} 失败: {ex.Message}");
                    _lastError = ex.Message;
                }
                _retryAt = Environment.TickCount64 + 1000;
            }
        }

        protected abstract void Activate();
        protected virtual void Tick() { }
        public virtual void Reset() { Generation = -1; _retryAt = 0; }

        public void Dispose()
        {
            lock (Owner._subscriptionGate)
            {
                if (Disposed) return;
                Disposed = true;
                Reset();
                Owner._subscriptions.Remove(this);
            }
        }
    }

    private sealed class InputSubscription<T>(PlcComponent owner, string path, Action<T> received)
        : Subscription(owner, path) where T : unmanaged
    {
        private IDisposable? _notification;
        private int _epoch;

        protected override void Activate()
        {
            int epoch = ++_epoch;
            long generation = Generation;
            _notification = Owner.SubscribeDevice<T>(Path, value =>
            {
                lock (Owner._subscriptionGate)
                {
                    if (Disposed || epoch != _epoch || generation != Owner.ConnectionGeneration || !Owner.IsConnected) return;
                    try { received(value); }
                    catch (Exception ex) { LogHelper.Warn("Plc", $"订阅回调 {Path}: {ex.Message}"); }
                }
            });
            var initial = Owner.ReadInitial<T>(Path);
            if (generation == Owner.ConnectionGeneration && Owner.IsConnected) received(initial);
        }

        public override void Reset()
        {
            ++_epoch;
            var notification = _notification;
            _notification = null;
            base.Reset();
            notification?.Dispose();
        }
    }

    private sealed class OutputSubscription<T>(PlcComponent owner, string path, Func<T> desired,
        Action<T> initialize, Action<T> written) : Subscription(owner, path) where T : unmanaged
    {
        public override bool IsOutput => true;
        private T _lastWritten;
        private bool _initialized;

        protected override void Activate()
        {
            _initialized = false;
            try
            {
                var initial = Owner.ReadInitial<T>(Path);
                if (Generation != Owner.ConnectionGeneration || !Owner.IsConnected) return;
                initialize(initial);
                _lastWritten = initial;
                _initialized = true;
            }
            catch { Generation = -1; throw; }
        }

        protected override void Tick()
        {
            if (!_initialized) return;
            var value = desired();
            if (EqualityComparer<T>.Default.Equals(value, _lastWritten)) return;
            byte[] data = new byte[Marshal.SizeOf<T>()];
            MemoryMarshal.Write(data, in value);
            long generation = Generation;
            if (!Owner.WriteDevice(Path, data))
                throw new InvalidOperationException($"写入 {Path} 失败");
            if (!Owner.IsConnected || generation != Owner.ConnectionGeneration) return;
            _lastWritten = value;
            written(value);
        }

        public override void Reset() { _initialized = false; base.Reset(); }
    }
}
