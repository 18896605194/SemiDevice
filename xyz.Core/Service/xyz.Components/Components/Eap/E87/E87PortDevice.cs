using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Secs.Gem.E87;
using xyz.Shared.Dtos;

namespace xyz.Components.Components;

/// <summary>
/// xyz.Secs 的 E87 要的一个端口：设备上的事实和命令问这个 LoadPort（连同它上面的载具），片号表读写晶圆账
/// </summary>
internal sealed class E87PortDevice : IE87Port
{
    private readonly ILoadPortAccess _loadPort;
    private readonly ILedgerManager? _ledger;

    public E87PortDevice(ILoadPortAccess loadPort, ILedgerManager? ledger)
    {
        _loadPort = loadPort;
        _ledger = ledger;
    }

    /// <summary>
    /// 这个端口对的 LoadPort
    /// </summary>
    public ILoadPortAccess LoadPort => _loadPort;

    public string Name => _loadPort.Name;

    public int SlotCount => _loadPort.SlotCount;

    public bool IsAutoMode => _loadPort.IsAutoMode;

    public bool IsLoaded => _loadPort.IsLoaded;

    public bool IsIdle => _loadPort.IsIdle;

    public E87TransferState LocalTransferState
    {
        get
        {
            switch (_loadPort.LocalTransferState)
            {
                case LoadPortTransferState.OutOfService:
                    return E87TransferState.OutOfService;
                case LoadPortTransferState.ReadyToLoad:
                    return E87TransferState.ReadyToLoad;
                case LoadPortTransferState.ReadyToUnload:
                    return E87TransferState.ReadyToUnload;
                default:
                    return E87TransferState.TransferBlocked;
            }
        }
    }

    public bool IsCarrierArrived => _loadPort._carrier.IsArrived;

    public string? ReadCarrierId => _loadPort._carrier.CarrierId;

    public IReadOnlyList<byte> SlotMap => _loadPort._carrier.SlotMap.Select(slot => (byte)slot).ToList();

    public IReadOnlyList<(string LotId, string SubstrateId)> ContentMap
    {
        get
        {
            var ledger = _ledger;
            if (ledger is null)
            {
                return Array.Empty<(string, string)>();
            }

            return ledger.GetSlots(_loadPort.Name)
                .Select(wafer => (wafer?.LotId ?? string.Empty, wafer?.WaferId ?? string.Empty))
                .ToList();
        }
    }

    public string? Load()
    {
        var result = _loadPort.Load();
        if (result.IsSuccess)
        {
            return null;
        }

        return $"{result.ErrorMessage}：{string.Join(", ", result.Args)}";
    }

    public string? Unload()
    {
        var result = _loadPort.Unload();
        if (result.IsSuccess)
        {
            return null;
        }

        return $"{result.ErrorMessage}：{string.Join(", ", result.Args)}";
    }

    public string? CheckUnload()
    {
        var rejected = _loadPort.CheckUnload();
        if (rejected is null)
        {
            return null;
        }

        return $"{rejected.ErrorMessage} [{string.Join(", ", rejected.Args)}]";
    }

    public void SetAutoMode(bool auto)
    {
        _loadPort.SetAutoMode(auto);
    }

    public bool ReadId()
    {
        return _loadPort._carrier.ReadId();
    }

    public void SetCarrierId(string carrierId)
    {
        _loadPort._carrier.SetId(carrierId);
    }

    public void SetCarrierIdStatus(E87CarrierIdState status)
    {
        switch (status)
        {
            case E87CarrierIdState.WaitingForHost:
                _loadPort._carrier.UpdateStatus(CarrierIdStatus.WaitingForHost, null);
                break;
            case E87CarrierIdState.Verified:
                _loadPort._carrier.UpdateStatus(CarrierIdStatus.Verified, null);
                break;
            case E87CarrierIdState.VerifyFailed:
                _loadPort._carrier.UpdateStatus(CarrierIdStatus.VerifyFailed, null);
                break;
        }
    }

    public void SetSlotMapStatus(E87SlotMapState status)
    {
        switch (status)
        {
            case E87SlotMapState.WaitingForHost:
                _loadPort._carrier.UpdateStatus(null, CarrierSlotMapStatus.WaitingForHost);
                break;
            case E87SlotMapState.Verified:
                _loadPort._carrier.UpdateStatus(null, CarrierSlotMapStatus.Verified);
                break;
            case E87SlotMapState.VerifyFailed:
                _loadPort._carrier.UpdateStatus(null, CarrierSlotMapStatus.VerifyFailed);
                break;
        }
    }

    /// <summary>
    /// Host 给的片号表写进晶圆账（片号、批次号），空槽、空项跳过
    /// </summary>
    public void WriteContentMap(IReadOnlyList<(string LotId, string SubstrateId)> content)
    {
        var ledger = _ledger;
        if (ledger is null)
        {
            return;
        }

        string name = _loadPort.Name;
        for (int index = 0; index < content.Count; index++)
        {
            var (lotId, substrateId) = content[index];
            if (ledger.Get(name, index + 1) is null)
            {
                continue;
            }

            if (substrateId.Length > 0)
            {
                ledger.SetWaferId(name, index + 1, substrateId);
            }

            if (lotId.Length > 0)
            {
                ledger.SetLotId(name, index + 1, lotId);
            }
        }
    }
}
