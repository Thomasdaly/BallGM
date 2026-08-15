using System.Text.Json;
using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;

namespace BallGM.Infrastructure.DraftAssets;

/// <summary>
/// Reads and writes a league's draft assets as versioned JSON. Like every other serializer here, it
/// never throws on bad content: a save or a data pack is untrusted input, so a malformed pick
/// produces a structured failure the loader can explain rather than a crash mid-load.
/// </summary>
public sealed class DraftAssetSerializer
{
    private const string MalformedFileCode = "draft_assets.malformed_file";
    private const string UnsupportedSchemaVersionCode = "draft_assets.unsupported_schema_version";
    private const string InvalidFieldCode = "draft_assets.invalid_field";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string Serialize(DraftAssetBook book)
    {
        ArgumentNullException.ThrowIfNull(book);

        var picks = book.Picks
            .OrderBy(pick => pick.DraftSeason.Year)
            .ThenBy(pick => pick.Round)
            .ThenBy(pick => pick.OriginalFranchiseId.Value, StringComparer.Ordinal)
            .Select(pick => ToEnvelope(pick, book.Ownership(pick.Id)!))
            .ToList();

        var envelope = new DraftAssetBookEnvelope(
            DraftAssetBookEnvelope.CurrentSchemaVersion,
            book.LeagueId.Value,
            picks);

        return JsonSerializer.Serialize(envelope, Options);
    }

    public DomainOperationResult<DraftAssetBook> Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        DraftAssetBookEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<DraftAssetBookEnvelope>(json, Options);
        }
        catch (JsonException exception)
        {
            return Fail(MalformedFileCode, $"The draft assets are not valid JSON: {exception.Message}");
        }

        if (envelope is null)
        {
            return Fail(MalformedFileCode, "The draft-asset payload did not contain a book.");
        }

        if (envelope.SchemaVersion != DraftAssetBookEnvelope.CurrentSchemaVersion)
        {
            return Fail(
                UnsupportedSchemaVersionCode,
                $"Draft-asset schema version {envelope.SchemaVersion} cannot be read by this build, which reads version {DraftAssetBookEnvelope.CurrentSchemaVersion}.");
        }

        if (envelope.Picks is null)
        {
            return Fail(MalformedFileCode, "The draft-asset payload declares no picks.");
        }

        try
        {
            var book = new DraftAssetBook(new LeagueId(envelope.LeagueId));

            // Two passes: every pick is registered before any encumbrance is attached, because a
            // swap right names a counterpart pick that may appear later in the file.
            foreach (var pickEnvelope in envelope.Picks)
            {
                var pickResult = DraftPick.Create(
                    new DraftPickId(pickEnvelope.PickId),
                    book.LeagueId,
                    new Season(pickEnvelope.DraftSeasonYear),
                    pickEnvelope.Round,
                    new FranchiseId(pickEnvelope.OriginalFranchiseId));

                if (pickResult.IsFailure)
                {
                    return DomainOperationResult<DraftAssetBook>.Failure(pickResult.Errors.ToArray());
                }

                var registerResult = book.Register(
                    pickResult.Value,
                    new FranchiseId(pickEnvelope.CurrentOwnerFranchiseId));

                if (registerResult.IsFailure)
                {
                    return DomainOperationResult<DraftAssetBook>.Failure(registerResult.Errors.ToArray());
                }
            }

            foreach (var pickEnvelope in envelope.Picks)
            {
                var encumbranceResult = ApplyEncumbrances(book, pickEnvelope);
                if (encumbranceResult.IsFailure)
                {
                    return DomainOperationResult<DraftAssetBook>.Failure(encumbranceResult.Errors.ToArray());
                }
            }

            return DomainOperationResult<DraftAssetBook>.Success(book);
        }
        catch (ArgumentException exception)
        {
            return Fail(InvalidFieldCode, exception.Message);
        }
    }

    private static DomainOperationResult ApplyEncumbrances(DraftAssetBook book, DraftPickEnvelope pickEnvelope)
    {
        var pickId = new DraftPickId(pickEnvelope.PickId);

        if (pickEnvelope.Obligation is { } obligationEnvelope)
        {
            var protectionResult = ToProtection(obligationEnvelope);
            if (protectionResult.IsFailure)
            {
                return DomainOperationResult.Failure(protectionResult.Errors.ToArray());
            }

            if (obligationEnvelope.ScheduleIndex > protectionResult.Value.ScheduleLength)
            {
                return DomainOperationResult.Failure(
                    new DomainError(
                        InvalidFieldCode,
                        $"Pick '{pickEnvelope.PickId}' declares schedule index {obligationEnvelope.ScheduleIndex}, past the end of a {protectionResult.Value.ScheduleLength}-draft protection schedule."));
            }

            var encumberResult = book.Encumber(
                pickId,
                new PickObligation(
                    new PickEncumbranceId(obligationEnvelope.EncumbranceId),
                    new FranchiseId(obligationEnvelope.BeneficiaryFranchiseId),
                    protectionResult.Value,
                    obligationEnvelope.ScheduleIndex));

            if (encumberResult.IsFailure)
            {
                return encumberResult;
            }
        }

        if (pickEnvelope.SwapRight is { } swapEnvelope)
        {
            var counterpartId = new DraftPickId(swapEnvelope.CounterpartPickId);
            if (book.Pick(counterpartId) is null)
            {
                return DomainOperationResult.Failure(
                    new DomainError(
                        InvalidFieldCode,
                        $"Pick '{pickEnvelope.PickId}' carries a swap right against pick '{swapEnvelope.CounterpartPickId}', which the file does not contain."));
            }

            return book.Encumber(
                pickId,
                new SwapRight(
                    new PickEncumbranceId(swapEnvelope.EncumbranceId),
                    new FranchiseId(swapEnvelope.HolderFranchiseId),
                    counterpartId));
        }

        return DomainOperationResult.Success;
    }

    private static DomainOperationResult<PickProtection> ToProtection(PickObligationEnvelope envelope)
    {
        if (!Enum.TryParse<PickProtectionFallbackKind>(envelope.FallbackKind, out var fallbackKind))
        {
            return DomainOperationResult<PickProtection>.Failure(
                new DomainError(
                    InvalidFieldCode,
                    $"'{envelope.FallbackKind}' is not a protection fallback this build knows."));
        }

        var fallbackResult = PickProtectionFallback.Rebuild(fallbackKind, envelope.ConvertsToRound);
        if (fallbackResult.IsFailure)
        {
            return DomainOperationResult<PickProtection>.Failure(fallbackResult.Errors.ToArray());
        }

        var levels = envelope.ProtectedSelections ?? [];
        return levels.Count == 0
            ? DomainOperationResult<PickProtection>.Success(PickProtection.Unprotected)
            : PickProtection.TopSelections(levels, fallbackResult.Value);
    }

    private static DraftPickEnvelope ToEnvelope(DraftPick pick, PickOwnership ownership)
    {
        var obligation = ownership.Obligation;
        var swap = ownership.PendingSwap;

        return new DraftPickEnvelope(
            pick.Id.Value,
            pick.DraftSeason.Year,
            pick.Round,
            pick.OriginalFranchiseId.Value,
            ownership.CurrentOwnerFranchiseId.Value,
            obligation is null
                ? null
                : new PickObligationEnvelope(
                    obligation.Id.Value,
                    obligation.BeneficiaryFranchiseId.Value,
                    obligation.Protection.ProtectedSelections,
                    obligation.Protection.Fallback.Kind.ToString(),
                    obligation.Protection.Fallback.ConvertsToRound,
                    obligation.ScheduleIndex),
            swap is null
                ? null
                : new SwapRightEnvelope(swap.Id.Value, swap.HolderFranchiseId.Value, swap.CounterpartPickId.Value));
    }

    private static DomainOperationResult<DraftAssetBook> Fail(string code, string message) =>
        DomainOperationResult<DraftAssetBook>.Failure(new DomainError(code, message));
}
