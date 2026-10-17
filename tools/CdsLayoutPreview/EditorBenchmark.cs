using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;

namespace CdsLayoutPreview;

internal static class EditorBenchmark
{
    public static void Run(PreviewPage page, string output)
    {
        const int count = 60;
        var report = new List<string> { "60 pointer moves per gesture. Measures UI-thread handler work, not display FPS." };
        void Measure(string name, Point start)
        {
            page.PointerDown(start);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            int paths = page.PathUpdateCount;
            var times = new List<double>();
            for (int i = 0; i < count; i++)
            {
                long time = Stopwatch.GetTimestamp();
                page.PointerMove(start + new Vector(20 + i % 20 * 3, i % 8 * 3));
                times.Add(Stopwatch.GetElapsedTime(time).TotalMilliseconds);
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            int pathUpdates = page.PathUpdateCount - paths;
            long release = Stopwatch.GetTimestamp();
            page.PointerUp();
            double releaseMs = Stopwatch.GetElapsedTime(release).TotalMilliseconds;
            report.Add(FormattableString.Invariant($"{name}: total={times.Sum():F2}ms mean={times.Average():F2}ms p95={times.Order().ElementAt(56):F2}ms allocation={bytes / 1024d / 1024:F2}MiB movePathRebuilds={pathUpdates} release={releaseMs:F2}ms"));
            page.Undo();
        }
        var tank = page.Document.Nodes.Single(node => node.Kind == "Tank");
        Measure("tank", new Point(tank.X + tank.Width / 2, tank.Y + tank.Height / 2));
        page.ObjectPicker.SelectedItem = page.Document.Pipes.First(pipe => pipe.Bends.Count > 0);
        Measure("bend", page.Document.Pipes.First(pipe => pipe.Bends.Count > 0).Bends[0]);
        File.WriteAllLines(Path.Combine(output, "editor-benchmark.txt"), report);
    }
}
