using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls
{
    /// <summary>
    /// Wafer 控件（由 GR 的 Disk 控件改名为 Wafer）。
    /// </summary>
    public partial class Wafer : UserControl
    {
        private DoubleAnimation _rotateAnimation;

        public Wafer()
        {
            InitializeComponent();
            _rotateAnimation = new DoubleAnimation();
            _rotateAnimation.RepeatBehavior = RepeatBehavior.Forever;
            rotateTransform.BeginAnimation(RotateTransform.AngleProperty, _rotateAnimation);

            rootGrid.ContextMenu.DataContext = this;
        }

        private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (CreateCommand == null && DeleteCommand == null)
            {
                e.Handled = true;
                return;
            }

            bool hasWafer = Data != null;
            createMenuItem.Visibility = hasWafer ? Visibility.Collapsed : Visibility.Visible;
            deleteMenuItem.Visibility = hasWafer ? Visibility.Visible : Visibility.Collapsed;
        }

        public object? Data
        {
            get { return (object?)GetValue(DataProperty); }
            set { SetValue(DataProperty, value); }
        }

        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
                nameof(Data), typeof(object), typeof(Wafer),
                new PropertyMetadata(null));

        public ICommand CreateCommand
        {
            get { return (ICommand)GetValue(CreateCommandProperty); }
            set { SetValue(CreateCommandProperty, value); }
        }

        public static readonly DependencyProperty CreateCommandProperty =
            DependencyProperty.Register(
                nameof(CreateCommand), typeof(ICommand), typeof(Wafer),
                new PropertyMetadata(null));

        public ICommand DeleteCommand
        {
            get { return (ICommand)GetValue(DeleteCommandProperty); }
            set { SetValue(DeleteCommandProperty, value); }
        }

        public static readonly DependencyProperty DeleteCommandProperty =
            DependencyProperty.Register(
                nameof(DeleteCommand), typeof(ICommand), typeof(Wafer),
                new PropertyMetadata(null));

        /// <summary>
        /// 建片/删片命令参数。
        /// </summary>
        public object CommandParameter
        {
            get { return GetValue(CommandParameterProperty); }
            set { SetValue(CommandParameterProperty, value); }
        }

        public static readonly DependencyProperty CommandParameterProperty =
            DependencyProperty.Register(
                nameof(CommandParameter), typeof(object), typeof(Wafer),
                new PropertyMetadata(null));

        /// <summary>
        /// "建片"菜单项使能。
        /// </summary>
        public bool CreateEnable
        {
            get { return (bool)GetValue(CreateEnableProperty); }
            set { SetValue(CreateEnableProperty, value); }
        }

        public static readonly DependencyProperty CreateEnableProperty =
            DependencyProperty.Register(
                nameof(CreateEnable), typeof(bool), typeof(Wafer),
                new PropertyMetadata(true));

        /// <summary>
        /// "删片"菜单项使能。
        /// </summary>
        public bool DeleteEnable
        {
            get { return (bool)GetValue(DeleteEnableProperty); }
            set { SetValue(DeleteEnableProperty, value); }
        }

        public static readonly DependencyProperty DeleteEnableProperty =
            DependencyProperty.Register(
                nameof(DeleteEnable), typeof(bool), typeof(Wafer),
                new PropertyMetadata(true));

        /// <summary>
        /// 无业务数据时是否仍显示 Wafer 图形。
        /// </summary>
        public bool IsDiskVisible
        {
            get => (bool)GetValue(IsDiskVisibleProperty);
            set => SetValue(IsDiskVisibleProperty, value);
        }

        public static readonly DependencyProperty IsDiskVisibleProperty =
            DependencyProperty.Register(
                nameof(IsDiskVisible), typeof(bool), typeof(Wafer),
                new PropertyMetadata(false));

        public Brush FillColor
        {
            get => (Brush)GetValue(FillColorProperty);
            set => SetValue(FillColorProperty, value);
        }

        public static readonly DependencyProperty FillColorProperty = DependencyProperty.Register(
                nameof(FillColor), typeof(Brush), typeof(Wafer),
                new PropertyMetadata(Brushes.Gray));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(
                nameof(Label), typeof(string), typeof(Wafer),
                new PropertyMetadata(string.Empty));

        public double RotationSpeed
        {
            get { return (double)GetValue(RotationSpeedProperty); }
            set { SetValue(RotationSpeedProperty, value); }
        }

        public static readonly DependencyProperty RotationSpeedProperty =
            DependencyProperty.Register("RotationSpeed", typeof(double), typeof(Wafer), new PropertyMetadata(0.0, OnRotationSpeedChanged));

        private static void OnRotationSpeedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            Wafer wafer = (Wafer)d;
            wafer.UpdateRotationAnimation();
        }

        public bool RotateClockwise
        {
            get { return (bool)GetValue(RotateClockwiseProperty); }
            set { SetValue(RotateClockwiseProperty, value); }
        }

        public static readonly DependencyProperty RotateClockwiseProperty =
            DependencyProperty.Register("RotateClockwise", typeof(bool), typeof(Wafer), new PropertyMetadata(true, OnRotateClockwiseChanged));

        private static void OnRotateClockwiseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            Wafer wafer = (Wafer)d;
            wafer.UpdateRotationAnimation();
        }

        /// <summary>
        /// wafer 速度旋转动画。
        /// </summary>
        private void UpdateRotationAnimation()
        {
            rotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);

            if (RotationSpeed > 1)
            {
                double currentAngle = rotateTransform.Angle % 360;
                rotateTransform.Angle = currentAngle;
                _rotateAnimation.From = currentAngle;
                _rotateAnimation.To = currentAngle + (RotateClockwise ? 360 : -360);
                _rotateAnimation.Duration = TimeSpan.FromSeconds(360 / Math.Abs(RotationSpeed));
                _rotateAnimation.AutoReverse = false;
                rotateTransform.BeginAnimation(RotateTransform.AngleProperty, _rotateAnimation);
            }
        }
    }
}
