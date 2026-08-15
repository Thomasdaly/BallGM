using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;

namespace BallGM.Domain.DraftAssets;

/// <summary>What one encumbrance did when its draft came around.</summary>
public enum ConveyanceOutcomeKind
{
    /// <summary>The swap was worth taking, so the two selections changed hands.</summary>
    SwapExercised = 0,

    /// <summary>The swap was not worth taking. The right is spent either way.</summary>
    SwapDeclined = 1,

    /// <summary>The protection did not hold, so the pick went to the franchise it was promised to.</summary>
    Conveyed = 2,

    /// <summary>The protection held. The obligation moved to the following draft.</summary>
    RolledOver = 3,

    /// <summary>The schedule ran out under a converting fallback: the obligation is now on a later round.</summary>
    ConvertedToLaterRound = 4,

    /// <summary>The schedule ran out under an extinguishing fallback: the obligation is gone for good.</summary>
    Extinguished = 5,
}

/// <summary>
/// One encumbrance's resolution, carrying the machine-readable rule code and the sentence a GM
/// reads — the same pairing the cap ledger returns, for the same reason: "your pick did not convey"
/// is only useful alongside *why*.
/// </summary>
public sealed record ConveyanceOutcome(
    DraftPickId PickId,
    PickEncumbranceId EncumbranceId,
    ConveyanceOutcomeKind Kind,
    int SelectionNumber,
    string RuleCode,
    string Explanation,
    FranchiseId? ResultingOwnerFranchiseId = null,
    DraftPickId? SuccessorPickId = null);

/// <summary>Where a pick's selection ended up once swap rights were settled, before protections were tested.</summary>
public sealed record EffectiveSelection(DraftPickId PickId, int Round, int SelectionNumber);

/// <summary>
/// Everything one draft's encumbrances did, in resolution order. Swap outcomes come first because
/// swaps resolve first — see <c>BallGM.Rules.DraftAssets.PickConveyanceEvaluator</c> for why that
/// ordering is a decision rather than an accident.
/// </summary>
public sealed record DraftConveyanceReport(
    Season DraftSeason,
    IReadOnlyList<ConveyanceOutcome> Outcomes,
    IReadOnlyList<EffectiveSelection> EffectiveSelections);
