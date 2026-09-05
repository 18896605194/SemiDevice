using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SimulatorHub;

/// <summary>勾选行 (绑定 DataGrid): 启用勾选 + 类型/实例只读 + 端口可编辑。</summary>
internal sealed class PickRow : INotifyPropertyChanged
{
    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled))); }
    }
    public string Type { get; set; } = "";
    public string Instance { get; set; } = "";
    public string Port { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// 载入预设时的实例勾选对话框 (照 XM UnifiedSimulator 的 EditInstances):
/// 全选/全不选 → 启用; allowDelete=false 用于编辑"上次布局" (非命名预设, 无可删)。
/// </summary>
public partial class LayoutPickerDialog : Window
{
    private readonly string _presetName;
    private readonly InstanceLayout[] _source;   // 原始布局 (保 AutoOpen), 与行一一对应
    private readonly List<PickRow> _rows = new();

    /// <summary>点了 [启用] 后要重建的实例 (按勾选顺序)。</summary>
    public List<InstanceLayout> Selected { get; } = new();

    /// <summary>点了 [删除该预设]。</summary>
    public bool DeleteRequested { get; private set; }

    public LayoutPickerDialog(string title, LayoutProfile profile, bool allowDelete)
    {
        InitializeComponent();

        _presetName = title;
        _source = profile.Instances.ToArray();
        Title = "载入预设 — " + title;
        TitleText.Text = $"预设 [{title}] · {_source.Length} 个实例";
        BtnDelete.Visibility = allowDelete ? Visibility.Visible : Visibility.Collapsed;

        foreach (var il in _source)
        {
            _rows.Add(new PickRow
            {
                Enabled = true,
                Type = il.Type == "Robot" ? "Robot" : il.Type == "Lp300" ? "LP300" : il.Type,
                Instance = il.Instance,
                Port = il.Port,
            });
        }
        Grid.ItemsSource = _rows;
    }

    private void BtnAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows) r.Enabled = true;
    }

    private void BtnNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows) r.Enabled = false;
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        Selected.Clear();
        for (int i = 0; i < _rows.Count; i++)
        {
            if (!_rows[i].Enabled) continue;
            Selected.Add(new InstanceLayout
            {
                Type = _source[i].Type,
                Instance = _source[i].Instance,
                Port = _rows[i].Port.Trim(),
                AutoOpen = _source[i].AutoOpen,
            });
        }
        if (Selected.Count == 0)
        {
            MessageBox.Show(this, "未勾选任何实例。", "载入预设", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        var ok = MessageBox.Show(this, $"删除预设 [{_presetName}] 的文件?\n(实例数据目录不受影响)",
            "删除预设", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;
        DeleteRequested = true;
        DialogResult = true;
    }
}
