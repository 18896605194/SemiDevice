using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace xyz.Client.Presentation.Controls
{
    /// <summary>
    /// 可自适应宿主尺寸的水平 Wafer 控件。
    /// </summary>
    public partial class HorizontalWafer : UserControl
    {
        public HorizontalWafer()
        {
            InitializeComponent();
            rootGrid.ContextMenu.DataContext = this;
        }

        private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            bool hasWafer = Data != null;
            bool showCreate = !hasWafer && CreateCommand != null;
            bool showDelete = hasWafer && DeleteCommand != null;

            if (!showCreate && !showDelete)
            {
                e.Handled = true;
                return;
            }

            createMenuItem.Visibility = showCreate ? Visibility.Visible : Visibility.Collapsed;
            deleteMenuItem.Visibility = showDelete ? Visibility.Visible : Visibility.Collapsed;
        }

        public object? Data
        {
            get { return GetValue(DataProperty); }
            set { SetValue(DataProperty, value); }
        }

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(
                nameof(Data), typeof(object), typeof(HorizontalWafer),
                new PropertyMetadata(null));

        public string Label
        {
            get { return (string)GetValue(LabelProperty); }
            set { SetValue(LabelProperty, value); }
        }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(
                nameof(Label), typeof(string), typeof(HorizontalWafer),
                new PropertyMetadata(string.Empty));

        public ICommand CreateCommand
        {
            get { return (ICommand)GetValue(CreateCommandProperty); }
            set { SetValue(CreateCommandProperty, value); }
        }

        public static readonly DependencyProperty CreateCommandProperty =
            DependencyProperty.Register(
                nameof(CreateCommand), typeof(ICommand), typeof(HorizontalWafer),
                new PropertyMetadata(null));

        public ICommand DeleteCommand
        {
            get { return (ICommand)GetValue(DeleteCommandProperty); }
            set { SetValue(DeleteCommandProperty, value); }
        }

        public static readonly DependencyProperty DeleteCommandProperty =
            DependencyProperty.Register(
                nameof(DeleteCommand), typeof(ICommand), typeof(HorizontalWafer),
                new PropertyMetadata(null));

        public object CommandParameter
        {
            get { return GetValue(CommandParameterProperty); }
            set { SetValue(CommandParameterProperty, value); }
        }

        public static readonly DependencyProperty CommandParameterProperty =
            DependencyProperty.Register(
                nameof(CommandParameter), typeof(object), typeof(HorizontalWafer),
                new PropertyMetadata(null));

        public bool CreateEnable
        {
            get { return (bool)GetValue(CreateEnableProperty); }
            set { SetValue(CreateEnableProperty, value); }
        }

        public static readonly DependencyProperty CreateEnableProperty =
            DependencyProperty.Register(
                nameof(CreateEnable), typeof(bool), typeof(HorizontalWafer),
                new PropertyMetadata(true));

        public bool DeleteEnable
        {
            get { return (bool)GetValue(DeleteEnableProperty); }
            set { SetValue(DeleteEnableProperty, value); }
        }

        public static readonly DependencyProperty DeleteEnableProperty =
            DependencyProperty.Register(
                nameof(DeleteEnable), typeof(bool), typeof(HorizontalWafer),
                new PropertyMetadata(true));
    }
}
