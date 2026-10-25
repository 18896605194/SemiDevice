using System.Numerics;
using Opc.Ua;

namespace xyz.Components.Components;

/// <summary>
/// OPC UA 取值
/// </summary>
internal static class OpcUaHelper
{
    /// <summary>
    /// 按数组在 PLC 里的类型做一个只有一格的数组（按下标写一格用）：整数类型四舍五入，超出范围返回 false
    /// </summary>
    public static bool TryMakeElement(BuiltInType type, double value, out Variant element)
    {
        element = default;
        switch (type)
        {
            case BuiltInType.SByte:
                if (!TryRound(value, out sbyte int8))
                {
                    return false;
                }

                element = new Variant(new ArrayOf<sbyte>(new[] { int8 }));
                return true;
            case BuiltInType.Byte:
                if (!TryRound(value, out byte uint8))
                {
                    return false;
                }

                element = new Variant(new ArrayOf<byte>(new[] { uint8 }));
                return true;
            case BuiltInType.Int16:
                if (!TryRound(value, out short int16))
                {
                    return false;
                }

                element = new Variant(new ArrayOf<short>(new[] { int16 }));
                return true;
            case BuiltInType.UInt16:
                if (!TryRound(value, out ushort uint16))
                {
                    return false;
                }

                element = new Variant(new ArrayOf<ushort>(new[] { uint16 }));
                return true;
            case BuiltInType.Int32:
                if (!TryRound(value, out int int32))
                {
                    return false;
                }

                element = new Variant(new ArrayOf<int>(new[] { int32 }));
                return true;
            case BuiltInType.UInt32:
                if (!TryRound(value, out uint uint32))
                {
                    return false;
                }

                element = new Variant(new ArrayOf<uint>(new[] { uint32 }));
                return true;
            case BuiltInType.Int64:
                if (!TryRound(value, out long int64))
                {
                    return false;
                }

                element = new Variant(new ArrayOf<long>(new[] { int64 }));
                return true;
            case BuiltInType.UInt64:
                if (!TryRound(value, out ulong uint64))
                {
                    return false;
                }

                element = new Variant(new ArrayOf<ulong>(new[] { uint64 }));
                return true;
            case BuiltInType.Float:
                float single = (float)value;
                if (!float.IsFinite(single))
                {
                    return false;
                }

                element = new Variant(new ArrayOf<float>(new[] { single }));
                return true;
            case BuiltInType.Double:
                element = new Variant(new ArrayOf<double>(new[] { value }));
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 四舍五入成整数类型，超出这个类型的范围返回 false
    /// </summary>
    private static bool TryRound<T>(double value, out T result) where T : struct, IBinaryInteger<T>, IMinMaxValue<T>
    {
        result = default;
        double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded < double.CreateChecked(T.MinValue) || rounded > double.CreateChecked(T.MaxValue))
        {
            return false;
        }

        result = T.CreateChecked(rounded);
        return true;
    }
}
