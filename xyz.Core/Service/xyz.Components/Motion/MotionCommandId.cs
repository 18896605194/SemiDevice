namespace xyz.Components.Motion
{
    /// <summary>
    /// plc 电机动作指令码
    /// </summary>
    public enum MotionCommandId : byte
    {
        /// <summary>
        /// 停止 
        /// </summary>
        Stop = 0,

        /// <summary>
        /// 复位驱动器故障
        /// </summary>
        Reset = 1,

        /// <summary>
        /// 回原点 (Param2=速度, Param3=加速度, Param4=减速度)
        /// </summary>
        Home = 2,

        /// <summary>
        /// 绝对定位 (Param1=目标位置, Param2=速度, Param3=加速度, Param4=减速度, Param5=最大速度)
        /// </summary>
        MoveTo = 3,

        /// <summary>
        /// 相对定位 (Param1=偏移量, 其余同 MoveTo)
        /// </summary>
        MoveBy = 4,

        /// <summary>
        /// 点动 (Param2=速度, Param3=加速度, Param4=减速度, Param5=最大速度)
        /// </summary>
        Jog = 5,

        /// <summary>
        /// 急停
        /// </summary>
        EStop = 6,

        /// <summary>
        /// 连续旋转 (Param2=速度, Param3=加速度, Param4=减速度, Param5=最大速度)
        /// </summary>
        Spin = 7,

        /// <summary>
        /// 扭矩模式 (Param8=扭矩)
        /// </summary>
        Torque = 8,

        /// <summary>
        /// 位置+扭矩复合模式
        /// </summary>
        PositionPlusTorque = 9,

        /// <summary>
        /// 复位扭矩
        /// </summary>
        ResetTorque = 10,
    }
}

