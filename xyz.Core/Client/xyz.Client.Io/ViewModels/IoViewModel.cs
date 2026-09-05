using System.Collections.ObjectModel;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Io.Models;

namespace xyz.Client.Io.ViewModels;

/// <summary>
/// IO 界面 ViewModel。
/// </summary>
public class IoViewModel : BaseViewModel
{
    #region Column

    public ObservableCollection<IoPointModel> Points { get; }

    private IoPointModel? _selectedPoint;

    public IoPointModel? SelectedPoint
    {
        get => _selectedPoint;
        set => SetProperty(ref _selectedPoint, value);
    }

    #endregion

    #region Command

    #endregion

    #region Service

    #endregion

    public IoViewModel()
    {
        Points = new ObservableCollection<IoPointModel>();
    }

    public override void Init()
    {
        Points.Clear();
        Points.Add(new IoPointModel { Name = "DI-01", Value = "1", Unit = "", IsOn = true, IsOutput = false, X = 120, Y = 60 });
        Points.Add(new IoPointModel { Name = "DI-02", Value = "0", Unit = "", IsOn = false, IsOutput = false, X = 240, Y = 60 });
        Points.Add(new IoPointModel { Name = "DI-03", Value = "1", Unit = "", IsOn = true, IsOutput = false, X = 360, Y = 60 });
        Points.Add(new IoPointModel { Name = "DI-04", Value = "0", Unit = "", IsOn = false, IsOutput = false, X = 480, Y = 60 });
        Points.Add(new IoPointModel { Name = "DO-01", Value = "1", Unit = "", IsOn = true, IsOutput = true, X = 120, Y = 160 });
        Points.Add(new IoPointModel { Name = "DO-02", Value = "0", Unit = "", IsOn = false, IsOutput = true, X = 240, Y = 160 });
        Points.Add(new IoPointModel { Name = "AI-01", Value = "12.5", Unit = "mA", IsOn = true, IsOutput = false, X = 360, Y = 160 });
        Points.Add(new IoPointModel { Name = "AI-02", Value = "4.2", Unit = "V", IsOn = true, IsOutput = false, X = 480, Y = 160 });
        Points.Add(new IoPointModel { Name = "AO-01", Value = "50", Unit = "%", IsOn = true, IsOutput = true, X = 120, Y = 260 });
        Points.Add(new IoPointModel { Name = "AO-02", Value = "0", Unit = "%", IsOn = false, IsOutput = true, X = 240, Y = 260 });

        SelectedPoint = Points.FirstOrDefault();
    }
}
