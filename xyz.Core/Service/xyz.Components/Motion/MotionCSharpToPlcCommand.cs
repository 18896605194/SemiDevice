using System.Runtime.InteropServices;

namespace xyz.Components.Motion
{

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct MotionCSharpToPlcCommand
    {
        /// <summary>
        /// 命令码 
        /// </summary>
        public byte Axis_Command;

        /// <summary>
        /// 伺服字节 (暂按 1=上使能 / 0=下使能)
        /// </summary>
        public byte Axis_Servo;

        /// <summary>
        /// 备用字节 1
        /// </summary>
        public byte Axis_Spare1;

        /// <summary>
        /// 备用字节 2
        /// </summary>
        public byte Axis_Spare2;

        /// <summary>
        /// 参数 1 (MoveTo=目标位置 / MoveBy=偏移量)
        /// </summary>
        public double Param1;

        /// <summary>
        /// 参数 2 (速度)
        /// </summary>
        public double Param2;

        /// <summary>
        /// 参数 3 (加速度)
        /// </summary>
        public double Param3;

        /// <summary>
        /// 参数 4 (减速度)
        /// </summary>
        public double Param4;

        /// <summary>
        /// 参数 5 (最大速度)
        /// </summary>
        public double Param5;

        /// <summary>
        /// 参数 6 (预留)
        /// </summary>
        public double Param6;

        /// <summary>
        /// 参数 7 (预留)
        /// </summary>
        public double Param7;

        /// <summary>
        /// 参数 8 (Torque=扭矩)
        /// </summary>
        public double Param8;

        /// <summary>
        /// 参数 9 (预留)
        /// </summary>
        public double Param9;

        /// <summary>
        /// 参数 10 
        /// </summary>
        public double Param10;

        /// <summary>
        /// 命令同步号 (每发一条命令 +1, PLC 检测变化锁存执行)
        /// </summary>
        public ulong Command_Sync_No;
    }
}

