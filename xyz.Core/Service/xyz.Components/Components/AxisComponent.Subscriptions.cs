using xyz.Common.Log;
using xyz.Components.Interfaces;
using xyz.Components.Motion;

namespace xyz.Components.Components;

public partial class AxisComponent
{
    #region 订阅字段

    private IPlc? _plc;
    private IDisposable? _inputSubscription;
    private IDisposable? _outputSubscription;
    private readonly object _subscriptionGate = new();
    private long _subscribedGeneration = -1;
    private int _subscriptionVersion;
    private long _subscribeRetryAt;

    #endregion

    #region 通信生命周期

    /// <summary>建立轴自身的通信订阅，不回零或使能。由装配在模块启动前调用。</summary>
    public bool Open(IPlc plc)
    {
        Close();
        if (string.IsNullOrWhiteSpace(SendPlcDataPath) || string.IsNullOrWhiteSpace(ReceivePlcDataPath))
        {
            return false;
        }

        lock (_axisGate)
        {
            _plc = plc;
        }

        EnsureSubscribed();
        return true;
    }

    public void Close()
    {
        lock (_subscriptionGate)
        {
            InvalidateSubscription();
            lock (_axisGate)
            {
                _plc = null;
            }
        }
    }

    /// <summary>由轴打开和自身扫描调用，断线或换连接时重新订阅。</summary>
    private void EnsureSubscribed()
    {
        lock (_subscriptionGate)
        {
            var plc = _plc;
            if (plc is null || !plc.IsConnected)
            {
                InvalidateSubscription();
                return;
            }

            if (_subscribedGeneration == plc.ConnectionGeneration)
            {
                return;
            }

            if (Environment.TickCount64 < _subscribeRetryAt)
            {
                return;
            }

            InvalidateSubscription();
            long generation = plc.ConnectionGeneration;
            int version = _subscriptionVersion;
            try
            {
                SubscribePlcData(plc, generation, version);

                lock (_axisGate)
                {
                    if (!IsCurrentSubscription(generation, version))
                    {
                        throw new InvalidOperationException("轴订阅期间 PLC 连接已变化");
                    }

                    _subscribedGeneration = generation;
                }
            }
            catch (Exception exception)
            {
                InvalidateSubscription();
                _subscribeRetryAt = Environment.TickCount64 + 1000;
                LogHelper.Warn("Axis", $"[{FullPath}] 订阅失败: {exception.Message}");
            }
        }
    }

    /// <summary>将轴的收发地址接到对应的数据回调。</summary>
    private void SubscribePlcData(IPlc plc, long generation, int version)
    {
        _outputSubscription = plc.SubscribeOutput<MotionCSharpToPlcCommand>(
            SendPlcDataPath,
            delegate
            {
                return GetDesiredCommand(generation, version);
            },
            delegate(MotionCSharpToPlcCommand command)
            {
                InitializeCommand(command, generation, version);
            },
            delegate(MotionCSharpToPlcCommand command)
            {
                OnCommandWritten(command, generation, version);
            });

        _inputSubscription = plc.SubscribeInput<MotionPlcToCSharpData>(
            ReceivePlcDataPath,
            delegate(MotionPlcToCSharpData status)
            {
                OnStatusReceived(status, generation, version);
            });
    }

    private bool IsCurrentSubscription(long generation, int version)
    {
        return _plc is { IsConnected: true }
            && _plc.ConnectionGeneration == generation
            && _subscriptionVersion == version;
    }

    private void InvalidateSubscription()
    {
        lock (_subscriptionGate)
        {
            lock (_axisGate)
            {
                ++_subscriptionVersion;
                _subscribedGeneration = -1;
                if (_operation is AxisOperationState.Pending or AxisOperationState.Running)
                {
                    _operation = AxisOperationState.Failed;
                }
            }

            // 先使回调失效，再释放 ADS 资源；释放过程不持有轴状态锁。
            _inputSubscription?.Dispose();
            _outputSubscription?.Dispose();
            _inputSubscription = null;
            _outputSubscription = null;
            _subscribeRetryAt = 0;
        }
    }

    #endregion
}
