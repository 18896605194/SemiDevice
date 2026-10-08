using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Components.Components;

/// <summary>
/// Job 命令被拒的错误码（ErrorCodes 里的 Job*）翻成 E5 的 ERRCODE，E40 / E94 回 Host 时用：
/// 错误文字写错误码名加参数（ASCII，Host 那边的人看得懂是哪一条）。
/// </summary>
internal static class JobErrors
{
    private static readonly IReadOnlyDictionary<string, ushort> Codes = new Dictionary<string, ushort>(StringComparer.Ordinal)
    {
        [ErrorCodes.JobNotInstalled] = E5Error.NotAvailable,
        [ErrorCodes.JobDisabled] = E5Error.NotAvailable,
        [ErrorCodes.WaferLedgerDisabled] = E5Error.NotAvailable,
        [ErrorCodes.JobCommandTimeout] = E5Error.Busy,
        [ErrorCodes.JobNotFound] = E5Error.UnknownObject,
        [ErrorCodes.JobProcessJobUnavailable] = E5Error.UnknownObject,
        [ErrorCodes.JobCommandNotAllowed] = E5Error.InvalidState,
        [ErrorCodes.JobEnding] = E5Error.InvalidState,
        [ErrorCodes.JobNotAuto] = E5Error.InvalidState,
        [ErrorCodes.JobLoadPortBusy] = E5Error.InvalidState,
        [ErrorCodes.JobCarrierBusy] = E5Error.InvalidState,
        [ErrorCodes.JobSlotClaimed] = E5Error.InvalidAttributeValue,
        [ErrorCodes.JobIdInvalid] = E5Error.InvalidAttributeValue,
        [ErrorCodes.JobIdDuplicate] = E5Error.IdentifierInUse,
        [ErrorCodes.JobWaferNotNormal] = E5Error.InvalidAttributeValue,
        [ErrorCodes.JobWaferProcessed] = E5Error.InvalidAttributeValue,
        [ErrorCodes.JobWaferOwned] = E5Error.InvalidAttributeValue,
        [ErrorCodes.JobLoadPortNotFound] = E5Error.LackOfMaterial,
        [ErrorCodes.JobCarrierNotReady] = E5Error.LackOfMaterial,
        [ErrorCodes.JobCarrierNotFound] = E5Error.LackOfMaterial,
        [ErrorCodes.JobSlotEmpty] = E5Error.LackOfMaterial,
        [ErrorCodes.JobNoWafers] = E5Error.LackOfMaterial,
        [ErrorCodes.JobReturnSlotUnavailable] = E5Error.LackOfMaterial,
        [ErrorCodes.JobSequenceNotFound] = E5Error.RecipeError,
        [ErrorCodes.JobSequenceSourceMismatch] = E5Error.RecipeError,
        [ErrorCodes.JobSequenceNoReturn] = E5Error.RecipeError,
        [ErrorCodes.JobRecipeNotFound] = E5Error.RecipeError,
        [ErrorCodes.JobStepNoStation] = E5Error.RecipeError,
        [ErrorCodes.JobStationTaskUnsupported] = E5Error.RecipeError,
        [ErrorCodes.OperationFaulted] = E5Error.FailedDuringProcessing,
    };

    /// <summary>被拒的结果 → 一条 E5 错误；没收录的码记成"现在不能处理"。</summary>
    public static E5Error Of(HandleResult result)
    {
        ushort code = Codes.TryGetValue(result.ErrorMessage, out var known) ? known : E5Error.NotAvailable;
        string text = result.Args.Count == 0 ? result.ErrorMessage : $"{result.ErrorMessage} {string.Join(' ', result.Args)}";
        return E5Error.Of(code, text);
    }
}
