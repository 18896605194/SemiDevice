using System.Collections.Concurrent;

namespace xyz.Modules;

/// <summary>
/// Job 账本：没删的 CJ（按队列顺序）、没结束的 PJ、最近删掉的 CJ（历史），以及"哪一片归哪个 PJ"。
/// 除归属表外只在 JobManager 的扫描线程里读写；归属表任意线程可查（搬运管理受理手动单、腔体手动起工艺时问）。
/// Job 名不分大小写（E39：Host 可以用任意大小写写 ObjID）。
/// </summary>
internal sealed class JobBook
{
    private readonly ConcurrentDictionary<Guid, string> _owners = new();

    /// <summary>没删的 CJ，按队列顺序：前面是已经选中、在跑、完成待删的，后面是排队的。</summary>
    public List<ControlJob> ControlJobs { get; } = [];

    /// <summary>没结束的 PJ，按建的先后（含还不归任何 CJ 的）。</summary>
    public List<ProcessJob> ProcessJobs { get; } = [];

    /// <summary>最近删掉的 CJ，新的在前（给界面看历史）。</summary>
    public List<ControlJob> History { get; } = [];

    /// <summary>内容版本：每改一次加 1，发布时带上。</summary>
    public long Version { get; private set; }

    /// <summary>有改动，这一拍要发布。</summary>
    public bool IsDirty { get; private set; }

    public void Touch()
    {
        IsDirty = true;
    }

    /// <summary>发布前取新版本号，清掉改动标记。</summary>
    public long NextVersion()
    {
        IsDirty = false;
        return ++Version;
    }

    public ControlJob? FindControlJob(string id)
    {
        return ControlJobs.FirstOrDefault(job => SameId(job.Id, id));
    }

    public ProcessJob? FindProcessJob(string id)
    {
        return ProcessJobs.FirstOrDefault(job => SameId(job.Id, id));
    }

    /// <summary>这个名字在没结束的 CJ、PJ 里有没有人用。</summary>
    public bool IsIdInUse(string id)
    {
        return FindControlJob(id) is not null || FindProcessJob(id) is not null;
    }

    /// <summary>这个 LoadPort 上没删的 CJ（一个 LoadPort 同时只有一个）。</summary>
    public ControlJob? ControlJobOn(string loadPort)
    {
        return ControlJobs.FirstOrDefault(job => string.Equals(job.LoadPort, loadPort, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>这一片现在归哪个 PJ；不归任何没结束的 PJ 返回 null。任意线程可调。</summary>
    public string? OwnerOf(Guid wafer)
    {
        return _owners.TryGetValue(wafer, out string? owner) ? owner : null;
    }

    /// <summary>PJ 建好：它的片记到它名下。</summary>
    public void Own(ProcessJob job)
    {
        foreach (var wafer in job.Wafers)
        {
            _owners[wafer.Id] = job.Id;
        }
    }

    /// <summary>PJ 结束：它名下的片放开。</summary>
    public void Release(ProcessJob job)
    {
        foreach (var wafer in job.Wafers)
        {
            _owners.TryRemove(new KeyValuePair<Guid, string>(wafer.Id, job.Id));
        }
    }

    /// <summary>CJ 删掉：从队列挪进历史（最多留 keep 个）。</summary>
    public void Archive(ControlJob job, int keep)
    {
        ControlJobs.Remove(job);
        History.Insert(0, job);
        while (History.Count > Math.Max(0, keep))
        {
            History.RemoveAt(History.Count - 1);
        }
    }

    public static bool SameId(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
