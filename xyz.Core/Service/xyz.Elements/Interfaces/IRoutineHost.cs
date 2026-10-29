namespace xyz.Elements.Interfaces;

/// <summary>
/// 挂得住动作的元件：框架扫一拍时给它一次把动作往前推的机会，排在元件自己的 OnScan 之前
/// </summary>
public interface IRoutineHost
{
    void StepRoutine();
}
