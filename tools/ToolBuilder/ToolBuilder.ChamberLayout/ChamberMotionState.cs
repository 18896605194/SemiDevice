using CommunityToolkit.Mvvm.ComponentModel;

namespace ChamberLayoutEditor;

/// <summary>单个组件的离线动作状态，不写入布局或撤销历史。</summary>
public sealed class ChamberMotionState : ObservableObject
{
    private double _reach;
    public double Reach { get => _reach; set => SetProperty(ref _reach, value); }
    private bool _raised;
    public bool Raised { get => _raised; set => SetProperty(ref _raised, value); }
    private bool _spray;
    public bool Spray { get => _spray; set => SetProperty(ref _spray, value); }
    private bool _spin;
    public bool Spin { get => _spin; set => SetProperty(ref _spin, value); }
    private bool _open;
    public bool Open { get => _open; set => SetProperty(ref _open, value); }
}
