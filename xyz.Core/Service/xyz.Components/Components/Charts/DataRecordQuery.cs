using SqlSugar;
using xyz.Components.Models;

namespace xyz.Components.Components;

/// <summary>
/// 查询
/// </summary>
internal static class DataRecordQuery
{
    /// <summary>
    /// 相邻两行隔了超过这么多个采样周期（软件没开、采样停过），中间插一个空点，曲线断开——不在空档上拉一条直线。
    /// </summary>
    private const double GapPeriods = 2.5;

    public static DataQueryResult Run(ISqlSugarClient db, IReadOnlyList<string> names, long from, long to,
        DateTime firstDay, DateTime lastDay, int maxPoints, long intervalMs, CancellationToken token)
    {
        int count = names.Count;
        if (count == 0 || to <= from)
        {
            return DataQueryResult.Empty;
        }

        intervalMs = Math.Max(1, intervalMs);
        maxPoints = Math.Max(2, maxPoints);
        bool decimate = (to - from) / intervalMs > maxPoints;
        IRowCollector collector = decimate
            ? new EnvelopeCollector(count, from, to, maxPoints / 2, intervalMs)
            : new RawCollector(count, (long)(intervalMs * GapPeriods), intervalMs);

        var stats = new Stat[count];
        var row = new double?[count];
        foreach (var (day, table) in DataRecordTable.ListTables(db))
        {
            if (day < firstDay || day > lastDay)
            {
                continue;
            }

            var columns = DataRecordTable.ColumnsOf(db, table).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var present = Enumerable.Range(0, count).Where(index => columns.Contains(names[index])).ToArray();
            if (present.Length == 0)
            {
                continue;
            }

            using var reader = DataRecordTable.Read(db, table, present.Select(index => names[index]).ToList(), from, to);
            int read = 0;
            while (reader.Read())
            {
                if ((++read & 0xFFF) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                Array.Clear(row);
                for (int column = 0; column < present.Length; column++)
                {
                    if (reader.IsDBNull(column + 1))
                    {
                        continue;
                    }

                    // 开关量列是 INTEGER，一样按 double 读。
                    double value = reader.GetDouble(column + 1);
                    row[present[column]] = value;
                    stats[present[column]].Add(value);
                }

                collector.Add(reader.GetInt64(0), row);
            }
        }

        token.ThrowIfCancellationRequested();
        var (times, values) = collector.Build();
        return new DataQueryResult
        {
            Times = times,
            Series = Enumerable.Range(0, count).Select(index => new DataQuerySeries
            {
                Name = names[index],
                Values = values[index],
                Min = stats[index].Count > 0 ? stats[index].Min : null,
                Max = stats[index].Count > 0 ? stats[index].Max : null,
                Avg = stats[index].Count > 0 ? stats[index].Sum / stats[index].Count : null,
                Count = stats[index].Count,
            }).ToList(),
            IsDecimated = decimate,
            BucketMs = collector.BucketMs,
        };
    }

    /// <summary>
    /// 导出：[from, to) 的原始行，不抽稀。按天表、再按 sliceMs 切成一段一段读，每段单独一次短查询：
    /// 内存里只有一段；WAL 下一直开着的读会让 WAL 文件合不回主库、越积越大，短读不会。读到一段给一段，这段没行就跳过。
    /// </summary>
    public static IEnumerable<DataExport> Export(ISqlSugarClient db, IReadOnlyList<string> names, long from, long to,
        DateTime firstDay, DateTime lastDay, long sliceMs, CancellationToken token)
    {
        if (names.Count == 0 || to <= from)
        {
            yield break;
        }

        foreach (var (day, table) in DataRecordTable.ListTables(db))
        {
            if (day < firstDay || day > lastDay)
            {
                continue;
            }

            var columns = DataRecordTable.ColumnsOf(db, table).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var present = Enumerable.Range(0, names.Count).Where(index => columns.Contains(names[index])).ToArray();
            if (present.Length == 0)
            {
                continue;
            }

            long dayFrom = Math.Max(from, LocalMilliseconds(day));
            long dayTo = Math.Min(to, LocalMilliseconds(day.AddDays(1)));
            for (long sliceFrom = dayFrom; sliceFrom < dayTo; sliceFrom += sliceMs)
            {
                token.ThrowIfCancellationRequested();
                var slice = ReadSlice(db, table, names, present, sliceFrom, Math.Min(dayTo, sliceFrom + sliceMs), token);
                if (slice is not null)
                {
                    yield return slice;
                }
            }
        }
    }

    /// <summary>
    /// 读一段：库里有的列照读，这天表里没有的列整列为 null；一行都没有返回 null
    /// </summary>
    private static DataExport? ReadSlice(ISqlSugarClient db, string table, IReadOnlyList<string> names, int[] present,
        long from, long to, CancellationToken token)
    {
        var times = new List<long>();
        var values = Enumerable.Range(0, names.Count).Select(_ => new List<double?>()).ToArray();
        using (var reader = DataRecordTable.Read(db, table, present.Select(index => names[index]).ToList(), from, to))
        {
            while (reader.Read())
            {
                if ((times.Count & 0xFFF) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                times.Add(reader.GetInt64(0));
                foreach (var list in values)
                {
                    list.Add(null);
                }

                for (int column = 0; column < present.Length; column++)
                {
                    if (!reader.IsDBNull(column + 1))
                    {
                        // 开关量列是 INTEGER，一样按 double 读。
                        values[present[column]][times.Count - 1] = reader.GetDouble(column + 1);
                    }
                }
            }
        }

        if (times.Count == 0)
        {
            return null;
        }

        return new DataExport
        {
            Times = times.ToArray(),
            Names = names,
            Values = values.Select(list => list.ToArray()).ToArray(),
        };
    }

    /// <summary>
    /// 本机日期的零点 → UTC 毫秒（日表按本机日期分）
    /// </summary>
    private static long LocalMilliseconds(DateTime day)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(day, DateTimeKind.Local)).ToUnixTimeMilliseconds();
    }

    private static float? ToFloat(double? value)
    {
        return value is not null ? (float)value.Value : null;
    }

    private struct Stat
    {
        public double Min;
        public double Max;
        public double Sum;
        public int Count;

        public void Add(double value)
        {
            if (Count == 0)
            {
                Min = value;
                Max = value;
            }
            else if (value < Min)
            {
                Min = value;
            }
            else if (value > Max)
            {
                Max = value;
            }

            Sum += value;
            Count++;
        }
    }

    private interface IRowCollector
    {
        long BucketMs { get; }

        void Add(long time, double?[] row);

        (long[] Times, float?[][] Values) Build();
    }

    /// <summary>
    /// 原样：一行一个点；隔得太远的两行之间插一个空点。
    /// </summary>
    private sealed class RawCollector(int count, long gapMs, long intervalMs) : IRowCollector
    {
        private readonly List<long> _times = [];
        private readonly List<float?>[] _values = Enumerable.Range(0, count).Select(_ => new List<float?>()).ToArray();
        private long? _last;

        public long BucketMs => 0;

        public void Add(long time, double?[] row)
        {
            long? last = _last;
            if (last is not null && time - last.Value > gapMs)
            {
                _times.Add(last.Value + intervalMs);
                foreach (var values in _values)
                {
                    values.Add(null);
                }
            }

            _times.Add(time);
            for (int index = 0; index < count; index++)
            {
                _values[index].Add(ToFloat(row[index]));
            }

            _last = time;
        }

        public (long[] Times, float?[][] Values) Build()
        {
            return (_times.ToArray(), _values.Select(values => values.ToArray()).ToArray());
        }
    }

    /// <summary>
    /// 抽稀：时间段等分成若干段，每条曲线每段记最小、最大值和各自出现的时刻，出图时两个点按先后排（段起点、段中点）。
    /// 一段里一个值都没有就给两个空点，曲线断开。
    /// </summary>
    private sealed class EnvelopeCollector : IRowCollector
    {
        private readonly int _count;
        private readonly long _from;
        private readonly int _buckets;
        private readonly double[] _minValue;
        private readonly double[] _maxValue;
        private readonly long[] _minTime;
        private readonly long[] _maxTime;
        private readonly bool[] _has;

        public EnvelopeCollector(int count, long from, long to, int buckets, long intervalMs)
        {
            buckets = Math.Max(1, buckets);
            BucketMs = Math.Max(intervalMs, (to - from + buckets - 1) / buckets);
            _buckets = (int)((to - from + BucketMs - 1) / BucketMs);
            _count = count;
            _from = from;
            int size = count * _buckets;
            _minValue = new double[size];
            _maxValue = new double[size];
            _minTime = new long[size];
            _maxTime = new long[size];
            _has = new bool[size];
        }

        public long BucketMs { get; }

        public void Add(long time, double?[] row)
        {
            long bucket = (time - _from) / BucketMs;
            if (bucket < 0 || bucket >= _buckets)
            {
                return;
            }

            for (int index = 0; index < _count; index++)
            {
                double? cell = row[index];
                if (cell is null)
                {
                    continue;
                }

                double value = cell.Value;
                int slot = index * _buckets + (int)bucket;
                if (!_has[slot])
                {
                    _has[slot] = true;
                    _minValue[slot] = _maxValue[slot] = value;
                    _minTime[slot] = _maxTime[slot] = time;
                }
                else if (value < _minValue[slot])
                {
                    _minValue[slot] = value;
                    _minTime[slot] = time;
                }
                else if (value > _maxValue[slot])
                {
                    _maxValue[slot] = value;
                    _maxTime[slot] = time;
                }
            }
        }

        public (long[] Times, float?[][] Values) Build()
        {
            var times = new long[_buckets * 2];
            for (int bucket = 0; bucket < _buckets; bucket++)
            {
                long start = _from + bucket * BucketMs;
                times[bucket * 2] = start;
                times[bucket * 2 + 1] = start + BucketMs / 2;
            }

            var values = new float?[_count][];
            for (int index = 0; index < _count; index++)
            {
                var series = values[index] = new float?[_buckets * 2];
                for (int bucket = 0; bucket < _buckets; bucket++)
                {
                    int slot = index * _buckets + bucket;
                    if (!_has[slot])
                    {
                        continue;
                    }

                    bool minFirst = _minTime[slot] <= _maxTime[slot];
                    series[bucket * 2] = (float)(minFirst ? _minValue[slot] : _maxValue[slot]);
                    series[bucket * 2 + 1] = (float)(minFirst ? _maxValue[slot] : _minValue[slot]);
                }
            }

            return (times, values);
        }
    }
}
