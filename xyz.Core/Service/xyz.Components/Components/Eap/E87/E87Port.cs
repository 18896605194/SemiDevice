using xyz.Common.Log;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// E87 的一个端口（照老 CTC：一个 LoadPort 一个对象，载具的状态就记在端口上）：挂 6 个状态机——
/// 搬运、存取方式、关联、载具 ID、槽图、取放。设备上的事实（在位、槽图、槽数、忙闲）都直接问 LoadPort，不另记。
/// 只在 E87 的锁里改。
/// </summary>
internal sealed class E87Port
{
    public E87Port(E87Component owner, ILoadPort device, byte id)
    {
        Owner = owner;
        Device = device;
        Carrier = device._carrier;
        Id = id;
        TransferMachine = new E87TransferStateMachine(this);
        AccessModeMachine = new E87AccessModeStateMachine(this);
        AssociationMachine = new E87AssociationStateMachine(this);
        CarrierIdMachine = new E87CarrierIdStateMachine(this);
        SlotMapMachine = new E87SlotMapStateMachine(this);
        AccessMachine = new E87AccessStateMachine(this);
    }

    public E87Component Owner { get; }

    public ILoadPort Device { get; }

    /// <summary>端口上这一盒载具（设备侧的载具事实和 E87 能下的命令），建端口时取一次，状态机和 Host 报文都从这儿用。</summary>
    public ICarrier Carrier { get; }

    /// <summary>PortID：按 sc.xml 里 LoadPort 的先后，从 1 开始。</summary>
    public byte Id { get; }

    /// <summary>
    /// E87 载具号（读到的，或 Host 给的）；没有载具为空。
    /// 载具拿走时 LoadPort 已经把号清了，删对象（#21）还得带号，所以 E87 自己记着。
    /// </summary>
    public string CarrierId { get; set; } = string.Empty;

    /// <summary>Host 取消或放行（CarrierRelease）了这一盒：卸好、端口空闲了就等取走。</summary>
    public bool Released { get; set; }

    public E87TransferStateMachine TransferMachine { get; }

    public E87AccessModeStateMachine AccessModeMachine { get; }

    public E87AssociationStateMachine AssociationMachine { get; }

    public E87CarrierIdStateMachine CarrierIdMachine { get; }

    public E87SlotMapStateMachine SlotMapMachine { get; }

    public E87AccessStateMachine AccessMachine { get; }

    /// <summary>端口上有 E87 载具对象（读到号或 Host 给了号）。</summary>
    public bool HasCarrier => CarrierIdMachine.State != E87CarrierIdState.NoCarrier;

    /// <summary>
    /// 认定了载具号：Load 起来读槽图（CTC 也是 ID 一认定就 Load）。动作攒到锁外做。
    /// "认定了才 Load"是 EAP 自己的条件，留在这里；端口能不能 Load（状态、账、载具在不在）由端口自己查、被拒带原因。
    /// </summary>
    public void LoadLater()
    {
        var device = Device;
        string carrierId = CarrierId;
        Owner.Later(() =>
        {
            // 等 Host 认定的时候操作员已经手动 Load 好了：要的就是 Load 着，不用再发
            if (device.IsLoaded)
            {
                return;
            }

            var result = device.Load();
            if (!result.IsSuccess)
            {
                LogHelper.Warn(Owner.Name,
                    $"{device.Name} 载具 {carrierId} 认定了，但 Load 被拒（{result.ErrorMessage}：{string.Join(", ", result.Args)}），等操作员处理");
            }
        });
    }

    /// <summary>Load 着的卸下来（关门、松开）；没 Load 的不用动。走自动跑货口径：SC AutoRunMapOnUnload 生效（带图对账）。动作攒到锁外做。</summary>
    public void UnloadLater(string why)
    {
        var device = Device;
        Owner.Later(() =>
        {
            if (device.IsLoaded && device.Unload() is null)
            {
                LogHelper.Warn(Owner.Name, $"{device.Name} {why}，但现在 Unload 不了（还在被机械手服务或端口状态不允许），等操作员处理");
            }
        });
    }
}
