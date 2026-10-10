using xyz.Components.Attributes;
using xyz.Drivers.Communication;
using xyz.Drivers.Loadport;
using xyz.Drivers.Loadport.FCD;
using xyz.Drivers.Loadport.FCD.Commands;

namespace xyz.Components.Components;

[Component(description: "富创得 LoadPort 驱动组件")]
public class FcdLoadPortComponent : LoadPortDriverComponent
{
    protected override ILoadPortDriver CreateDriver()
    {
        return new FcdLoadPortDriver(new FrameCommunication(CreateTransport(), new FcdFrameCodec()));
    }

    protected override LoadPortCommand CreateLoad()
    {
        return new FcdLoadCommand(_driver!);
    }

    protected override LoadPortCommand CreateUnload()
    {
        return new FcdUnloadCommand(_driver!);
    }

    protected override LoadPortCommand CreateMap()
    {
        return new FcdMapCommand(_driver!);
    }

    protected override LoadPortCommand CreateHome()
    {
        return new FcdHomeCommand(_driver!);
    }

    protected override LoadPortCommand CreateClamp()
    {
        return new FcdClampCommand(_driver!);
    }

    protected override LoadPortCommand CreateUnclamp()
    {
        return new FcdUnclampCommand(_driver!);
    }

    protected override LoadPortCommand CreateStop()
    {
        return new FcdAbortCommand(_driver!);
    }

    protected override LoadPortCommand CreateResetDrive()
    {
        return new FcdResetCommand(_driver!);
    }

    protected override LoadPortCommand CreateQueryStatus()
    {
        return new FcdGetStateCommand(_driver!);
    }

    protected override LoadPortCommand CreateQueryVersion()
    {
        return new FcdGetVersionCommand(_driver!);
    }
}
