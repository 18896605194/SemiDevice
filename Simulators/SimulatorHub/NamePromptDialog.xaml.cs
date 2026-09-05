using System.Windows;

namespace SimulatorHub;

/// <summary>单行名称输入 (保存布局预设用)。返回 null = 取消。回车=确定 (IsDefault 按钮)。</summary>
public partial class NamePromptDialog : Window
{
    public NamePromptDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
    }

    public static string? Show(Window owner, string title, string label, string defaultText)
    {
        var dlg = new NamePromptDialog { Owner = owner, Title = title };
        dlg.LabelText.Text = label;
        dlg.InputBox.Text = defaultText;
        return dlg.ShowDialog() == true ? dlg.InputBox.Text : null;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
