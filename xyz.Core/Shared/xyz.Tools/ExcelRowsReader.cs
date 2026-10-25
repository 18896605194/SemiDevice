using System.Collections;
using System.Data.Common;

namespace xyz.Tools;

/// <summary>
/// 把一行一行的值包成读取器交给 MiniExcel：列名从这里拿，所以没有行的表也有表头
/// </summary>
internal sealed class ExcelRowsReader(string[] columns, IEnumerable<object?[]> rows) : DbDataReader
{
    private readonly IEnumerator<object?[]> _rows = rows.GetEnumerator();

    public override int FieldCount => columns.Length;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override int Depth => 0;

    public override bool HasRows => true;

    public override bool IsClosed => false;

    public override int RecordsAffected => -1;

    public override bool Read()
    {
        return _rows.MoveNext();
    }

    public override bool NextResult()
    {
        return false;
    }

    public override string GetName(int ordinal)
    {
        return columns[ordinal];
    }

    public override int GetOrdinal(string name)
    {
        return Array.IndexOf(columns, name);
    }

    public override object GetValue(int ordinal)
    {
        var row = _rows.Current;
        if (ordinal >= row.Length)
        {
            return DBNull.Value;
        }

        return row[ordinal] ?? DBNull.Value;
    }

    public override int GetValues(object[] values)
    {
        int count = Math.Min(values.Length, columns.Length);
        for (int index = 0; index < count; index++)
        {
            values[index] = GetValue(index);
        }

        return count;
    }

    public override bool IsDBNull(int ordinal)
    {
        return GetValue(ordinal) is DBNull;
    }

    public override Type GetFieldType(int ordinal)
    {
        return typeof(object);
    }

    public override string GetDataTypeName(int ordinal)
    {
        return nameof(Object);
    }

    public override DateTime GetDateTime(int ordinal)
    {
        return (DateTime)GetValue(ordinal);
    }

    public override double GetDouble(int ordinal)
    {
        return Convert.ToDouble(GetValue(ordinal));
    }

    public override string GetString(int ordinal)
    {
        return Convert.ToString(GetValue(ordinal)) ?? string.Empty;
    }

    public override bool GetBoolean(int ordinal)
    {
        return Convert.ToBoolean(GetValue(ordinal));
    }

    public override byte GetByte(int ordinal)
    {
        return Convert.ToByte(GetValue(ordinal));
    }

    public override char GetChar(int ordinal)
    {
        return Convert.ToChar(GetValue(ordinal));
    }

    public override decimal GetDecimal(int ordinal)
    {
        return Convert.ToDecimal(GetValue(ordinal));
    }

    public override float GetFloat(int ordinal)
    {
        return Convert.ToSingle(GetValue(ordinal));
    }

    public override short GetInt16(int ordinal)
    {
        return Convert.ToInt16(GetValue(ordinal));
    }

    public override int GetInt32(int ordinal)
    {
        return Convert.ToInt32(GetValue(ordinal));
    }

    public override long GetInt64(int ordinal)
    {
        return Convert.ToInt64(GetValue(ordinal));
    }

    public override Guid GetGuid(int ordinal)
    {
        return (Guid)GetValue(ordinal);
    }

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        throw new NotSupportedException();
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        throw new NotSupportedException();
    }

    public override IEnumerator GetEnumerator()
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rows.Dispose();
        }

        base.Dispose(disposing);
    }
}
