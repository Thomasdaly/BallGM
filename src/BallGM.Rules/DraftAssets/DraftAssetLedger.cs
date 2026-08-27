using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.DraftAssets;

/// <summary>
/// Turns the ownership book into the board a GM reads: franchises down, the next several drafts
/// across, and — the part that matters — the protection spelled out in words next to every
/// conditional asset. A board that lists who owns what but not what is riding on it is how a GM
/// trades into a lottery pick they have already sold.
/// <para>
/// The prose lives here rather than in the client for the same reason the cap ledger's threshold
/// explanations do: what a protection means is a rules question, and two screens inventing their
/// own wording is two chances to word it wrongly.
/// </para>
/// </summary>
public sealed class DraftAssetLedger
{
    private const string InvalidDraftCountCode = "draft_board.invalid_draft_count";

    public DomainOperationResult<DraftAssetBoard> BuildBoard(
        DraftAssetBook book,
        IReadOnlyList<FranchiseDraftIdentity> franchises,
        Season firstDraftSeason,
        DraftRules draftRules)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(franchises);
        ArgumentNullException.ThrowIfNull(firstDraftSeason);
        ArgumentNullException.ThrowIfNull(draftRules);

        // A league with no draft gets a board with no drafts on it, not a failure: "there is nothing
        // to show here" is a true answer about a league, and refusing to build the board would stop
        // the rest of the screen loading over a section that league does not have.
        if (!draftRules.HasDraft)
        {
            return DomainOperationResult<DraftAssetBoard>.Success(
                new DraftAssetBoard(firstDraftSeason, DraftCount: 0, RoundCount: 0, Rows: []));
        }

        var draftCount = draftRules.TradableFutureDraftHorizon;
        if (draftCount <= 0)
        {
            return DomainOperationResult<DraftAssetBoard>.Failure(
                new DomainError(InvalidDraftCountCode, "The board must cover at least one draft."));
        }

        var names = franchises.ToDictionary(franchise => franchise.FranchiseId, franchise => franchise.Name);
        var rows = new List<DraftAssetBoardRow>(franchises.Count);

        foreach (var franchise in franchises)
        {
            var cells = new List<DraftAssetBoardCell>(draftCount);

            for (var offset = 0; offset < draftCount; offset++)
            {
                var season = new Season(firstDraftSeason.Year + offset);
                cells.Add(new DraftAssetBoardCell(season, BuildCell(book, franchise.FranchiseId, season, draftRules, names)));
            }

            rows.Add(new DraftAssetBoardRow(franchise.FranchiseId, cells));
        }

        return DomainOperationResult<DraftAssetBoard>.Success(
            new DraftAssetBoard(firstDraftSeason, draftCount, draftRules.RoundCount, rows));
    }

    private static IReadOnlyList<PickAssetLine> BuildCell(
        DraftAssetBook book,
        FranchiseId franchiseId,
        Season season,
        DraftRules draftRules,
        IReadOnlyDictionary<FranchiseId, string> names)
    {
        var lines = new List<PickAssetLine>();

        // The franchise's own picks first, in round order: what it is scheduled to keep, owe, or has
        // already given up. A missing round is simply a draft the book does not cover.
        for (var round = 1; round <= draftRules.RoundCount; round++)
        {
            var ownPick = book.Find(season, round, franchiseId);
            if (ownPick is null)
            {
                continue;
            }

            var ownership = book.Ownership(ownPick.Id)!;
            lines.Add(DescribeOwnPick(ownPick, ownership, season, names));
        }

        // Then everything acquired from elsewhere, and every right held over somebody else's pick.
        // Collected separately so they can be ordered on a stable value: identifiers are minted per
        // load, so ordering on them reshuffles the cell between launches.
        var acquired = new List<PickAssetLine>();

        foreach (var pick in book.PicksInDraft(season))
        {
            if (pick.OriginalFranchiseId == franchiseId)
            {
                continue;
            }

            var ownership = book.Ownership(pick.Id)!;
            var originalName = NameOf(pick.OriginalFranchiseId, names);

            if (ownership.CurrentOwnerFranchiseId == franchiseId)
            {
                var obligation = ownership.Obligation;
                acquired.Add(new PickAssetLine(
                    pick.Id,
                    pick.Round,
                    pick.OriginalFranchiseId,
                    ownership.CurrentOwnerFranchiseId,
                    obligation is null ? PickControlState.Acquired : PickControlState.OwedAway,
                    pick.OriginalFranchiseId,
                    obligation is null
                        ? $"Acquired from {originalName}."
                        : $"Acquired from {originalName}, and owed on to {NameOf(obligation.BeneficiaryFranchiseId, names)}: {DescribeProtection(obligation)}.",
                    obligation is null ? null : DescribeHeldOutcome(obligation, season, names)));
                continue;
            }

            if (ownership.PendingSwap?.HolderFranchiseId == franchiseId)
            {
                acquired.Add(new PickAssetLine(
                    pick.Id,
                    pick.Round,
                    pick.OriginalFranchiseId,
                    ownership.CurrentOwnerFranchiseId,
                    PickControlState.SwapRightHeld,
                    pick.OriginalFranchiseId,
                    $"Swap right held over {originalName}'s round {pick.Round} pick.",
                    $"If that pick lands ahead of the counterpart selection, the two selections change places when the {season.Year} draft is settled."));
                continue;
            }

            var incomingObligation = ownership.Obligation;
            if (incomingObligation?.BeneficiaryFranchiseId == franchiseId)
            {
                acquired.Add(new PickAssetLine(
                    pick.Id,
                    pick.Round,
                    pick.OriginalFranchiseId,
                    ownership.CurrentOwnerFranchiseId,
                    PickControlState.Incoming,
                    pick.OriginalFranchiseId,
                    $"Owed by {NameOf(ownership.CurrentOwnerFranchiseId, names)}: {DescribeProtection(incomingObligation)}.",
                    DescribeHeldOutcome(incomingObligation, season, names)));
            }
        }

        lines.AddRange(acquired
            .OrderBy(line => line.Round)
            .ThenBy(line => NameOf(line.OriginalFranchiseId, names), StringComparer.Ordinal)
            .ThenBy(line => line.State));

        return lines;
    }

    private static PickAssetLine DescribeOwnPick(
        DraftPick pick,
        PickOwnership ownership,
        Season season,
        IReadOnlyDictionary<FranchiseId, string> names)
    {
        var obligation = ownership.Obligation;
        var swap = ownership.PendingSwap;

        if (ownership.CurrentOwnerFranchiseId != pick.OriginalFranchiseId)
        {
            return new PickAssetLine(
                pick.Id,
                pick.Round,
                pick.OriginalFranchiseId,
                ownership.CurrentOwnerFranchiseId,
                PickControlState.TradedAway,
                ownership.CurrentOwnerFranchiseId,
                $"Traded to {NameOf(ownership.CurrentOwnerFranchiseId, names)} outright. Nothing conditional remains.",
                null);
        }

        if (obligation is not null)
        {
            var swapClause = swap is null
                ? string.Empty
                : $" A swap right held by {NameOf(swap.HolderFranchiseId, names)} settles first and can change where this pick lands.";

            return new PickAssetLine(
                pick.Id,
                pick.Round,
                pick.OriginalFranchiseId,
                ownership.CurrentOwnerFranchiseId,
                PickControlState.OwedAway,
                obligation.BeneficiaryFranchiseId,
                $"Owed to {NameOf(obligation.BeneficiaryFranchiseId, names)}: {DescribeProtection(obligation)}.{swapClause}",
                DescribeHeldOutcome(obligation, season, names));
        }

        if (swap is not null)
        {
            return new PickAssetLine(
                pick.Id,
                pick.Round,
                pick.OriginalFranchiseId,
                ownership.CurrentOwnerFranchiseId,
                PickControlState.SwapEncumbered,
                swap.HolderFranchiseId,
                $"{NameOf(swap.HolderFranchiseId, names)} may swap this selection for their own.",
                $"If this pick lands ahead of theirs, they take this selection and leave theirs behind in the {season.Year} draft.");
        }

        return new PickAssetLine(
            pick.Id,
            pick.Round,
            pick.OriginalFranchiseId,
            ownership.CurrentOwnerFranchiseId,
            PickControlState.OwnedOutright,
            null,
            null,
            null);
    }

    private static string DescribeProtection(PickObligation obligation)
    {
        var level = obligation.CurrentProtectionLevel;
        if (level is null)
        {
            return obligation.Protection.IsUnprotected
                ? "unprotected"
                : "unprotected, its protection schedule already spent";
        }

        var rolled = obligation.ScheduleIndex == 0
            ? string.Empty
            : $", already rolled over {obligation.ScheduleIndex} {(obligation.ScheduleIndex == 1 ? "draft" : "drafts")}";

        return $"protected through selection {level.Value}{rolled}";
    }

    /// <summary>
    /// The half of the board a GM actually plans against: not "this pick is protected" but "and here
    /// is what happens to it if that protection holds".
    /// </summary>
    private static string DescribeHeldOutcome(
        PickObligation obligation,
        Season season,
        IReadOnlyDictionary<FranchiseId, string> names)
    {
        var level = obligation.CurrentProtectionLevel;
        if (level is null)
        {
            return $"It conveys to {NameOf(obligation.BeneficiaryFranchiseId, names)} in the {season.Year} draft wherever it lands.";
        }

        var nextYear = season.Year + 1;
        if (obligation.HasRemainingSchedule)
        {
            var nextLevel = obligation.Protection.LevelAt(obligation.ScheduleIndex + 1);
            return $"If it lands in the top {level.Value}, it stays and the obligation rolls to the {nextYear} draft protected through selection {nextLevel}.";
        }

        return obligation.Protection.Fallback.Kind switch
        {
            PickProtectionFallbackKind.ConveysUnprotected =>
                $"If it lands in the top {level.Value}, it stays this year and the obligation rolls to the {nextYear} draft unprotected — it conveys there regardless.",
            PickProtectionFallbackKind.ConvertsToRound =>
                $"If it lands in the top {level.Value}, it stays for good and the obligation converts to an unprotected round {obligation.Protection.Fallback.ConvertsToRound} pick in the {nextYear} draft.",
            _ =>
                $"If it lands in the top {level.Value}, it stays for good and the obligation extinguishes — nothing further is owed.",
        };
    }

    private static string NameOf(FranchiseId franchiseId, IReadOnlyDictionary<FranchiseId, string> names) =>
        names.TryGetValue(franchiseId, out var name) ? name : franchiseId.Value;
}
