namespace xyz.Modules.Enums;

/// <summary>
/// 整机状态码（系统模块）：未初始化、初始化中、空闲（Manual）、停止中、出错直接用 <see cref="ModuleState"/> 的，
/// 这里只加自动的两种和全部回片。
/// </summary>
public class SystemState : ModuleState
{
    /// <summary>自动空闲：切了 Auto，没有在跑的 Job。</summary>
    public const int AutoIdle = 100;

    /// <summary>自动运行：切了 Auto，有在跑的 Job（执行中、暂停、中止中）；Job 都结束了回自动空闲。</summary>
    public const int AutoRunning = 110;

    /// <summary>全部回片中：机内的片回各自来源 LoadPort 的原槽，回完回空闲。</summary>
    public const int Returning = 120;
}

/// <summary>
/// 整机动作。整机停止叫 Abort（界面上是 Stop）：模块基类只认这个名字能顶替在途动作（停得下初始化、回片）。
/// </summary>
public enum SystemAction
{
    Init,
    Auto,
    Manual,
    ReturnAll,
    Abort,
}
