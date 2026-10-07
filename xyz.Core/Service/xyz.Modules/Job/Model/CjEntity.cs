namespace xyz.Modules;

/// <summary>CJ 运行实体，包含 CJ 对象和它自己的状态机。</summary>
public sealed class CjEntity
{
    public ControlJob ControlJob { get; }

    public CjStateMachine StateMachine { get; }

    public CjEntity(ControlJob controlJob)
    {
        ControlJob = controlJob;
        StateMachine = new CjStateMachine(controlJob);
    }
}
