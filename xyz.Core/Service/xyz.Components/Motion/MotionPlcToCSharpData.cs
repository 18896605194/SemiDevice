using System.Runtime.InteropServices;

namespace xyz.Components.Motion
{

    /// <summary>
    /// plc给上位机
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct MotionPlcToCSharpData
    {
        // Status codes
        public uint Status_Code;
        public uint Error_Code;
        public uint Warning_Code;
        public uint Spare_Code;

        // Float data
        public double Current_Speed;
        public double Current_Torque;
        public double Current_Position;
        public double Target_Position;
        public double Target_Speed;
        public double Spare_Data;

        // Boolean flags (each BYTE in PLC = 1 byte in C#)
        public byte Is_Err;
        public byte Is_Warning;
        public byte Is_Homed;
        public byte Is_Stopping;
        public byte Is_Stopped;
        public byte Is_Ready;
        public byte Is_Servo_On;
        public byte Is_In_Position;
        public byte Is_Home_Sensor_ON;
        public byte Is_PLimit_On;
        public byte Is_NLimit_On;
        public byte Is_Busy;
        public byte Spare_Boolean_2;
        public byte Spare_Boolean_3;
        public byte Spare_Boolean_4;
        public byte Spare_Boolean_5;
    }
}

