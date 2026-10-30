using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using xyz.Tools;
using xyz._35021.Client.Manual.ViewModels;

namespace xyz._35021.Client.Manual.Views;

public partial class ChamberManualView : UserControl
{
    public ChamberManualView(string module)
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/xyz.35021.Client.Presentation;component/Styles/ChamberStyles.xaml") });
        InitializeComponent();
        DataContext = (IocHelper.ServiceProvider ?? throw new InvalidOperationException("Client services are not initialized."))
            .GetRequiredKeyedService<ChamberManualViewModel>(module);
    }
}
