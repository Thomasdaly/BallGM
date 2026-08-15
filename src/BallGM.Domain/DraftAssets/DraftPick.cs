using BallGM.Domain.Common;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;

namespace BallGM.Domain.DraftAssets;

public sealed record DraftPickId
{
    public DraftPickId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// A draft pick's identity, and nothing else: which league's draft, which draft, which round, and
/// which franchise the selection originally belonged to. Every field is immutable, because none of
/// them can change — a pick traded twice is still the same asset, originally belonging to the same
/// franchise.
/// <para>
/// Who controls it today is <see cref="PickOwnership"/>, deliberately a separate type. Merging the
/// two is how a pick system ends up unable to answer "whose pick was this originally", and that
/// question is what every protection is written against: a top-4 protection means top 4 of the
/// <em>original</em> franchise's selection, no matter how many hands the asset has passed through.
/// </para>
/// </summary>
public sealed class DraftPick
{
    private const string InvalidRoundCode = "draft_pick.invalid_round";

    private DraftPick(DraftPickId id, LeagueId leagueId, Season draftSeason, int round, FranchiseId originalFranchiseId)
    {
        Id = id;
        LeagueId = leagueId;
        DraftSeason = draftSeason;
        Round = round;
        OriginalFranchiseId = originalFranchiseId;
    }

    /// <summary>
    /// Creates a pick identity. A round number outside the league's structure is a business-rule
    /// failure rather than a throw: draft picks are data-pack surface, and untrusted content
    /// declaring a fourth round in a two-round league must fail explainably, not crash the loader.
    /// </summary>
    public static DomainOperationResult<DraftPick> Create(
        DraftPickId id,
        LeagueId leagueId,
        Season draftSeason,
        int round,
        FranchiseId originalFranchiseId)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(leagueId);
        ArgumentNullException.ThrowIfNull(draftSeason);
        ArgumentNullException.ThrowIfNull(originalFranchiseId);

        if (round < 1)
        {
            return DomainOperationResult<DraftPick>.Failure(
                new DomainError(InvalidRoundCode, $"Draft round must be 1 or greater, but was {round}."));
        }

        return DomainOperationResult<DraftPick>.Success(
            new DraftPick(id, leagueId, draftSeason, round, originalFranchiseId));
    }

    public DraftPickId Id { get; }

    public LeagueId LeagueId { get; }

    public Season DraftSeason { get; }

    public int Round { get; }

    /// <summary>
    /// The franchise whose on-court finish decides where this selection lands. Never reassigned:
    /// this is the half of a pick that trading cannot touch.
    /// </summary>
    public FranchiseId OriginalFranchiseId { get; }

    public override string ToString() =>
        $"{DraftSeason.Year} round {Round} ({OriginalFranchiseId.Value})";
}
