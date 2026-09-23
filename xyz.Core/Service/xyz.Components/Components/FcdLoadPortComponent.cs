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
        return new FcdLoadCommand(Driver!);
    }

    protected override LoadPortCommand CreateUnload()
    {
        return new FcdUnloadCommand(Driver!);
    }

    protected override LoadPortCommand CreateHome()
    {
        return new FcdHomeCommand(Driver!);
    }

    protected override LoadPortCommand CreateClamp()
    {
        return new FcdClampCommand(Driver!);
    }

    protected override LoadPortCommand CreateUnclamp()
    {
        return new FcdUnclampCommand(Driver!);
    }

    protected override LoadPortCommand CreateStop()
    {
        return new FcdAbortCommand(Driver!);
    }

    protected override LoadPortCommand CreateResetDrive()
    {
        return new FcdResetCommand(Driver!);
    }

    protected override LoadPortCommand CreateQueryStatus()
    {
        return new FcdGetStateCommand(Driver!);
    }

    protected override LoadPortCommand CreateQueryVersion()
    {
        return new FcdGetVersionCommand(Driver!);
    }
}
