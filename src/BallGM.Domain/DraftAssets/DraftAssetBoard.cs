using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;

namespace BallGM.Domain.DraftAssets;

/// <summary>How a franchise stands on one pick.</summary>
public enum PickControlState
{
    /// <summary>Its own pick, unencumbered. It will make this selection.</summary>
    OwnedOutright = 0,

    /// <summary>Its own pick, promised to somebody else — conditionally, if a protection sits on it.</summary>
    OwedAway = 1,

    /// <summary>Somebody else's pick that this franchise now controls.</summary>
    Acquired = 2,

    /// <summary>Its own pick, which another franchise may take in exchange for theirs.</summary>
    SwapEncumbered = 3,

    /// <summary>A right this franchise holds over another franchise's pick.</summary>
    SwapRightHeld = 4,

    /// <summary>Its own pick, already gone — traded outright, with nothing conditional left to wait on.</summary>
    TradedAway = 5,

    /// <summary>
    /// Somebody else's pick, promised to this franchise but not conveyed yet. Deliberately not the
    /// same state as <see cref="Acquired"/>: an asset that may never arrive is not an asset in hand,
    /// and a board that words the two the same way is a board that oversells a rebuild.
    /// </summary>
    Incoming = 6,
}

/// <summary>
/// A franchise as the board names it. The board is a read-oriented projection, so it carries the
/// name alongside the identifier rather than making every consumer re-resolve it.
/// </summary>
public sealed record FranchiseDraftIdentity(FranchiseId FranchiseId, string Name);

/// <summary>
/// One pick as it appears on a franchise's board, with the protection spelled out. A board that
/// shows ownership but hides protection is the board that gets a GM traded into a lottery they
/// already sold, so <see cref="ProtectionSummary"/> and <see cref="OutcomeIfProtectionHolds"/> are
/// part of the model rather than something the view is trusted to remember to render.
/// </summary>
public sealed record PickAssetLine(
    DraftPickId PickId,
    int Round,
    FranchiseId OriginalFranchiseId,
    FranchiseId CurrentOwnerFranchiseId,
    PickControlState State,
    FranchiseId? CounterpartyFranchiseId,
    string? ProtectionSummary,
    string? OutcomeIfProtectionHolds);

/// <summary>One franchise's assets in one future draft.</summary>
public sealed record DraftAssetBoardCell(Season DraftSeason, IReadOnlyList<PickAssetLine> Assets);

/// <summary>One franchise's row: what it holds across the next several drafts.</summary>
public sealed record DraftAssetBoardRow(FranchiseId FranchiseId, IReadOnlyList<DraftAssetBoardCell> Drafts);

/// <summary>
/// Franchises down, the next several drafts across. Built by
/// <c>BallGM.Rules.DraftAssets.DraftAssetLedger</c> from the ownership book, so the screen and the
/// rules layer cannot disagree about who owns what.
/// </summary>
public sealed record DraftAssetBoard(
    Season FirstDraftSeason,
    int DraftCount,
    int RoundCount,
    IReadOnlyList<DraftAssetBoardRow> Rows);
