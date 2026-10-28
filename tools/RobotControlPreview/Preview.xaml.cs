using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

namespace RobotControlPreview;

public partial class Preview : Window
{
    public ObservableCollection<RobotArmModel> Arms { get; } = [];

    public Preview()
    {
        InitializeComponent();
        Arms.Add(new RobotArmModel { Arm = 1, Extension = 1,
            Wafer = new WaferModel { Slot = 1, State = WaferStates.Idle, LpSlot = "LP1-01" } });
        Arms.Add(new RobotArmModel { Arm = 2,
            Wafer = new WaferModel { Slot = 2, State = "Completed", LpSlot = "LP1-02" } });
        Flat.Arms = Solid.Arms = Arms;
        LoadPort.SlotCount = 25; LoadPort.IsPresent = LoadPort.IsPlaced = true;
        LoadPort.Wafers = Enumerable.Range(1, 25).Where(i => i != 6 && i != 12).Select(i =>
            new WaferModel { Slot = i, State = WaferStates.Idle, LpSlot = "LP1-" + i.ToString("D2") }).ToList();
        var devices = new ChamberDeviceDataModel();
        devices.Update(new ChamberDeviceDataDto
        {
            Module = "Chamber1",
            Door = new ChamberCylinderDto { Path = "Chamber1.Door", Position = CylinderPosition.Opened },
            Bowls = [new ChamberCylinderDto { Path = "Chamber1.Bowl1", Position = CylinderPosition.Closed }],
            Spin = new ChamberAxisDto { Path = "Chamber1.SpinMotor" },
            Arms = Enumerable.Range(1, 2).Select(i => new ChamberArmDto
            {
                Path = "Chamber1.Arm" + i, Reach = i == 1 ? 1 : 0, EdgeReach = .5,
                Lift = new ChamberCylinderDto { Path = "Chamber1.Arm" + i + ".Lift", Position = CylinderPosition.Closed },
                Nozzles = [new ChamberNozzleDto { Path = "Chamber1.Arm" + i + ".Nozzle_DIW", Chemical = "DIW" },
                    new ChamberNozzleDto { Path = "Chamber1.Arm" + i + ".Nozzle_SC1", Chemical = "SC1" }],
            }).ToList(),
        });
        Chamber.Devices = devices;
        Chamber.Wafer = new WaferModel { Slot = 1, State = "Process", LpSlot = "LP1-03" };
    }

    private void OnStructure(object sender, RoutedEventArgs args)
    {
        var type = Flat.ArmType == RobotArmType.Linear ? RobotArmType.FrogLeg : RobotArmType.Linear;
        Flat.ArmType = Solid.ArmType = type;
        Structure.SetResourceReference(ContentControl.ContentProperty, type == RobotArmType.Linear ? "preview.frog" : "preview.linear");
    }

    private void OnDirection(object sender, RoutedEventArgs args) => Flat.Rotation = Solid.Rotation =
        (RobotDirection)int.Parse(((Button)sender).Tag.ToString()!);

    private void OnArm(object sender, RoutedEventArgs args)
    {
        int number = int.Parse(((Button)sender).Tag.ToString()!);
        foreach (var arm in Arms) arm.Extension = arm.Arm == number ? 1 : 0;
    }

    private void OnRetract(object sender, RoutedEventArgs args)
    {
        foreach (var arm in Arms) arm.Extension = 0;
    }

    private void OnWafer(object sender, RoutedEventArgs args) => Arms[0].Wafer = Arms[0].Wafer is null
        ? new WaferModel { Slot = 1, State = WaferStates.Idle, LpSlot = "LP1-01" } : null;
}
