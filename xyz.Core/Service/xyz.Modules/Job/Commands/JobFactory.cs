using System.Globalization;
using xyz.Components.Enums;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 建 Job：校验（LoadPort、载具、每一片、名字、上限、流程配方和工艺配方、路线上的站点、回片槽）全过了，
/// 才在内存里造出 CJ / PJ 和配方快照；有一项不过就整个不建，什么都不留（不改账、不占片）。
/// 挂进账本、报事件由命令处理做。只在 JobManager 的扫描线程上用。
/// </summary>
internal sealed class JobFactory
{
    /// <summary>E39 ObjID 最长 80 个字符。</summary>
    private const int MaxIdLength = 80;

    /// <summary>E39 ObjID 不能用的字符。</summary>
    private const string ForbiddenIdChars = "?*~>:";

    private readonly JobRuntime _runtime;

    public JobFactory(JobRuntime runtime)
    {
        _runtime = runtime;
    }

    /// <summary>
    /// 本地建 Job：一个 LoadPort 上的一篮，每槽一个流程配方；相同流程配方的槽分成一个 PJ（PJ 按投片顺序排），整篮一个 CJ。
    /// </summary>
    public JobCommandResult? TryBuildLocal(LocalJobRequest request, out ControlJob control, out List<ProcessJob> processes)
    {
        control = null!;
        processes = [];
        var book = _runtime.Book;
        var environment = _runtime.Environment;
        string portName = request.LoadPort.Trim();
        var port = environment.LoadPort(portName);
        if (port is null)
        {
            return JobCommandResult.Reject(ErrorCodes.JobLoadPortNotFound, portName);
        }

        portName = port.Name;
        var busy = book.ControlJobOn(portName);
        if (busy is not null)
        {
            return JobCommandResult.Reject(ErrorCodes.JobLoadPortBusy, portName, busy.Id);
        }

        if (!environment.IsCarrierReady(portName))
        {
            return JobCommandResult.Reject(ErrorCodes.JobCarrierNotReady, portName);
        }

        var selected = request.SlotSequences
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim());
        if (selected.Count == 0)
        {
            return JobCommandResult.Reject(ErrorCodes.JobNoWafers, portName);
        }

        string controlId = string.IsNullOrWhiteSpace(request.LotId)
            ? $"CJ-{portName}-{DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}"
            : request.LotId.Trim();
        var idError = CheckNewId(controlId)
            ?? CheckCapacity(selected.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count(), controlJobs: 1);
        if (idError is not null)
        {
            return idError;
        }

        // 按取片顺序排槽：PJ 的先后、PJ 里片的先后都照它
        var order = port.PickOrder == SlotPickOrder.TopDown
            ? selected.Keys.OrderByDescending(slot => slot)
            : selected.Keys.OrderBy(slot => slot);

        var groups = new List<(string Sequence, List<int> Slots)>();
        foreach (int slot in order)
        {
            string sequence = selected[slot];
            var group = groups.FirstOrDefault(item => string.Equals(item.Sequence, sequence, StringComparison.OrdinalIgnoreCase));
            if (group.Slots is null)
            {
                group = (sequence, []);
                groups.Add(group);
            }

            group.Slots.Add(slot);
        }

        var returnSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (var (sequence, slots) in groups)
        {
            index++;
            string processId = $"{controlId}-{index.ToString(CultureInfo.InvariantCulture)}";
            var idTaken = CheckNewId(processId);
            if (idTaken is not null)
            {
                return idTaken;
            }

            var processError = TryBuildProcessJob(processId, portName, slots, sequence, autoStart: true, JobCommandSource.Local,
                returnSlots, out var process);
            if (processError is not null)
            {
                return processError;
            }

            processes.Add(process);
        }

        control = new ControlJob
        {
            Id = controlId,
            LoadPort = portName,
            CarrierId = port.CarrierId,
            CarrierInstance = port.Carrier?.Id,
            LotId = string.IsNullOrWhiteSpace(request.LotId) ? null : request.LotId.Trim(),
            AutoStart = request.AutoStart,
            CreatedBy = JobCommandSource.Local,
        };
        return null;
    }

    /// <summary>
    /// 建一个 PJ（Host 的 PRJobCreate）：按载具号找 LoadPort（现在要求载具已经在 LoadPort 上）。
    /// </summary>
    public JobCommandResult? TryBuildProcessJob(ProcessJobSpec spec, JobCommandSource source, out ProcessJob process)
    {
        process = null!;
        string id = spec.Id.Trim();
        var idError = CheckNewId(id) ?? CheckCapacity(processJobs: 1, controlJobs: 0);
        if (idError is not null)
        {
            return idError;
        }

        string carrier = spec.CarrierId.Trim();
        var port = FindPortByCarrier(carrier);
        if (port is null)
        {
            return JobCommandResult.Reject(ErrorCodes.JobCarrierNotFound, carrier);
        }

        if (!_runtime.Environment.IsCarrierReady(port.Name))
        {
            return JobCommandResult.Reject(ErrorCodes.JobCarrierNotReady, port.Name);
        }

        if (spec.Slots.Count == 0)
        {
            return JobCommandResult.Reject(ErrorCodes.JobNoWafers, port.Name);
        }

        var returnSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return TryBuildProcessJob(id, port.Name, spec.Slots.Distinct().ToList(), spec.Sequence.Trim(), spec.AutoStart, source,
            returnSlots, out process);
    }

    /// <summary>
    /// 建一个 CJ（Host 的 Create Object）：引用的 PJ 都要已经建好、还没归别的 CJ、在同一个 LoadPort 上（第一版一个 CJ 一个载具）。
    /// </summary>
    public JobCommandResult? TryBuildControlJob(ControlJobSpec spec, JobCommandSource source, out ControlJob control,
        out List<ProcessJob> processes)
    {
        control = null!;
        processes = [];
        string id = spec.Id.Trim();
        var idError = CheckNewId(id);
        if (idError is not null)
        {
            return idError;
        }

        var capacityError = CheckCapacity(processJobs: 0, controlJobs: 1);
        if (capacityError is not null)
        {
            return capacityError;
        }

        string? portName = null;
        foreach (string processId in spec.ProcessJobs)
        {
            var process = _runtime.Book.FindProcessJob(processId.Trim());
            if (process is null || process.ControlJob is not null || process.State != PrJobState.QueuedPooled
                || processes.Contains(process))
            {
                return JobCommandResult.Reject(ErrorCodes.JobProcessJobUnavailable, processId.Trim());
            }

            string port = process.Wafers.Count > 0 ? process.Wafers[0].SourcePort : string.Empty;
            if (portName is not null && !string.Equals(portName, port, StringComparison.OrdinalIgnoreCase))
            {
                return JobCommandResult.Reject(ErrorCodes.JobProcessJobUnavailable, processId.Trim());
            }

            portName = port;
            processes.Add(process);
        }

        if (processes.Count == 0 || portName is null)
        {
            return JobCommandResult.Reject(ErrorCodes.JobNoWafers, id);
        }

        var busy = _runtime.Book.ControlJobOn(portName);
        if (busy is not null)
        {
            return JobCommandResult.Reject(ErrorCodes.JobLoadPortBusy, portName, busy.Id);
        }

        var loadPort = _runtime.Environment.LoadPort(portName);
        control = new ControlJob
        {
            Id = id,
            LoadPort = portName,
            CarrierId = loadPort?.CarrierId,
            CarrierInstance = loadPort?.Carrier?.Id,
            AutoStart = spec.AutoStart,
            CreatedBy = source,
        };
        return null;
    }

    /// <summary>
    /// 造一个 PJ：每一片查过（有片、正常片、没做过、没在别的 Job 里），配方快照取好，回片槽定好。
    /// returnSlots 是这一次建 Job 里已经分出去的回片槽（几个 PJ 不能回同一个槽）。
    /// </summary>
    private JobCommandResult? TryBuildProcessJob(string id, string portName, IReadOnlyList<int> slots, string sequence, bool autoStart,
        JobCommandSource source, HashSet<string> returnSlots, out ProcessJob process)
    {
        process = null!;
        var ledger = _runtime.Environment.Ledger;
        if (ledger is null || !ledger.IsEnable)
        {
            return JobCommandResult.Reject(ErrorCodes.WaferLedgerDisabled);
        }

        var recipeError = TryTakeRecipe(sequence, portName, out var recipe);
        if (recipeError is not null)
        {
            return recipeError;
        }

        var wafers = new List<JobWafer>();
        foreach (int slot in slots)
        {
            string slotText = slot.ToString(CultureInfo.InvariantCulture);
            var wafer = ledger.Get(portName, slot);
            if (wafer is null)
            {
                return JobCommandResult.Reject(ErrorCodes.JobSlotEmpty, portName, slotText);
            }

            if (wafer.Status != WaferStatus.Normal)
            {
                return JobCommandResult.Reject(ErrorCodes.JobWaferNotNormal, portName, slotText, wafer.WaferId, wafer.Status.ToString());
            }

            if (wafer.ProcessState != WaferProcessState.Idle)
            {
                return JobCommandResult.Reject(ErrorCodes.JobWaferProcessed, portName, slotText, wafer.WaferId, wafer.ProcessState.ToString());
            }

            string? owner = _runtime.Book.OwnerOf(wafer.Id);
            if (owner is not null)
            {
                return JobCommandResult.Reject(ErrorCodes.JobWaferOwned, wafer.WaferId, owner);
            }

            var returnError = PickReturnSlot(recipe, portName, slot, returnSlots, out string returnPort);
            if (returnError is not null)
            {
                return returnError;
            }

            wafers.Add(new JobWafer
            {
                Id = wafer.Id,
                Name = wafer.WaferId,
                SourcePort = portName,
                SourceSlot = slot,
                ReturnPort = returnPort,
                ReturnSlot = slot,
                Station = portName,
                Slot = slot,
            });
        }

        process = new ProcessJob
        {
            Id = id,
            Recipe = recipe,
            AutoStart = autoStart,
            CreatedBy = source,
        };
        process.Wafers.AddRange(wafers);
        return null;
    }

    /// <summary>
    /// 取配方快照：流程配方在库里；第 1 步勾了来源 LoadPort；最后一步有回片的 LoadPort；
    /// 中间每一步的工艺配方在工艺配方库里（没装库只认名字），能去的站点里去掉没装、停用、机械手到不了、跑不了这个配方的，至少剩一个。
    /// </summary>
    private JobCommandResult? TryTakeRecipe(string sequence, string portName, out JobRecipe recipe)
    {
        recipe = null!;
        var data = SequenceComponent.Current?.Find(sequence);
        if (data is null || data.Steps.Count < 3)
        {
            return JobCommandResult.Reject(ErrorCodes.JobSequenceNotFound, sequence);
        }

        var environment = _runtime.Environment;
        var first = data.Steps[0];
        var last = data.Steps[^1];
        if (!first.Stations.Contains(portName, StringComparer.OrdinalIgnoreCase))
        {
            return JobCommandResult.Reject(ErrorCodes.JobSequenceSourceMismatch, data.Name, portName);
        }

        var returnPorts = last.Stations.Where(name => environment.LoadPort(name) is not null).ToList();
        if (returnPorts.Count == 0)
        {
            return JobCommandResult.Reject(ErrorCodes.JobSequenceNoReturn, data.Name);
        }

        var library = ProcessRecipeComponent.Current;
        var steps = new List<JobRouteStep>();
        for (int index = 1; index < data.Steps.Count - 1; index++)
        {
            var step = data.Steps[index];
            string recipeName = step.Recipe.Trim();
            ProcessRecipeData? snapshot = null;
            if (recipeName.Length > 0 && library is not null)
            {
                snapshot = library.Find(recipeName);
                if (snapshot is null)
                {
                    return JobCommandResult.Reject(ErrorCodes.JobRecipeNotFound, data.Name, recipeName);
                }
            }

            var stations = step.Stations.Where(name =>
            {
                var module = environment.Module(name);
                if (module is null || !module.IsEnabled || module is not ITransferStation || !environment.IsReachable(name))
                {
                    return false;
                }

                return snapshot is null || library is null || module is not IProcessStation || library.FindMismatch(snapshot, name) is null;
            }).ToList();
            if (stations.Count == 0)
            {
                return JobCommandResult.Reject(ErrorCodes.JobStepNoStation, data.Name,
                    (index + 1).ToString(CultureInfo.InvariantCulture), recipeName);
            }

            steps.Add(new JobRouteStep { Group = step.Group, Stations = stations, RecipeName = recipeName, Recipe = snapshot });
        }

        recipe = new JobRecipe
        {
            SequenceName = data.Name,
            SequenceIndex = data.Index,
            SequenceRevision = data.Revision,
            SourcePorts = first.Stations.ToList(),
            ReturnPorts = returnPorts,
            Steps = steps,
        };
        return null;
    }

    /// <summary>
    /// 定回片槽（流程配方页定下的规则）：来源 LoadPort 在最后一步里就回原槽；不在就放到最后一步第一个有载具的 LoadPort 的同号槽，
    /// 那个槽要空着，也不能是别的没结束的片要回的槽。
    /// </summary>
    private JobCommandResult? PickReturnSlot(JobRecipe recipe, string portName, int slot, HashSet<string> taken, out string returnPort)
    {
        returnPort = portName;
        string slotText = slot.ToString(CultureInfo.InvariantCulture);
        if (recipe.ReturnPorts.Contains(portName, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var environment = _runtime.Environment;
        string? port = recipe.ReturnPorts.FirstOrDefault(name => environment.IsCarrierReady(name));
        if (port is null)
        {
            return JobCommandResult.Reject(ErrorCodes.JobReturnSlotUnavailable, recipe.ReturnPorts[0], slotText);
        }

        var target = environment.LoadPort(port);
        string key = TransferManager.SlotKey(port, slot);
        bool claimed = _runtime.Book.ProcessJobs.Any(job => job.Wafers.Any(wafer =>
            wafer.Phase != JobWaferPhase.Done && string.Equals(wafer.ReturnPort, port, StringComparison.OrdinalIgnoreCase)
            && wafer.ReturnSlot == slot));
        if (target is null || slot > target.SlotCount || environment.Ledger?.Get(port, slot) is not null || claimed || !taken.Add(key))
        {
            return JobCommandResult.Reject(ErrorCodes.JobReturnSlotUnavailable, port, slotText);
        }

        returnPort = target.Name;
        return null;
    }

    private BaseLoadPortModule? FindPortByCarrier(string carrier)
    {
        if (carrier.Length == 0)
        {
            return null;
        }

        return _runtime.Environment.LoadPorts.FirstOrDefault(port =>
            string.Equals(port.CarrierId, carrier, StringComparison.OrdinalIgnoreCase) && port.IsPodPlaced);
    }

    /// <summary>
    /// 新名字：E39 的 ObjID（1~80 个 ASCII 可见字符和空格，不能有 ? * ~ &gt; :），而且没结束的 Job 里没人用。
    /// </summary>
    private JobCommandResult? CheckNewId(string id)
    {
        bool valid = id.Length >= 1 && id.Length <= MaxIdLength
            && id.All(ch => ch >= ' ' && ch <= '~' && !ForbiddenIdChars.Contains(ch));
        if (!valid)
        {
            return JobCommandResult.Reject(ErrorCodes.JobIdInvalid, id);
        }

        return _runtime.Book.IsIdInUse(id) ? JobCommandResult.Reject(ErrorCodes.JobIdDuplicate, id) : null;
    }

    /// <summary>
    /// 没结束的 PJ、没删的 CJ 加上要建的不能超过上限（SC ProcessJobCapacity / ControlJobCapacity；以后 Host 问剩几个位置也按它答）。
    /// </summary>
    private JobCommandResult? CheckCapacity(int processJobs, int controlJobs)
    {
        int processCapacity = Math.Max(1, _runtime.Limits.ProcessJobCapacity());
        if (processJobs > 0 && _runtime.Book.ProcessJobs.Count + processJobs > processCapacity)
        {
            return JobCommandResult.Reject(ErrorCodes.JobQueueFull, processCapacity.ToString(CultureInfo.InvariantCulture));
        }

        int controlCapacity = Math.Max(1, _runtime.Limits.ControlJobCapacity());
        if (controlJobs > 0 && _runtime.Book.ControlJobs.Count + controlJobs > controlCapacity)
        {
            return JobCommandResult.Reject(ErrorCodes.JobQueueFull, controlCapacity.ToString(CultureInfo.InvariantCulture));
        }

        return null;
    }
}
